#!/usr/bin/env bash
# Mac에서 EC2로 게임 서버(Linux 빌드)와 Node 백엔드를 한 번에 배포한다.
#
#   deploy/deploy.sh [EC2_IP] [옵션]
#
#   EC2_IP          Learner Lab 세션마다 바뀌는 공인 IP. 생략하면 지난번 IP(deploy/.ec2-host)를 쓴다.
#   --skip-build    빌드 없이 마지막 빌드 결과를 올린다.
#   --server-only   게임 서버만 배포한다.
#   --backend-only  백엔드만 배포한다(빌드도 생략).
#
# 환경 변수: EC2_KEY(기본 ~/Downloads/rp-key.pem), BUILD_DIR(기본 ../Build/rolypoly_build)
set -euo pipefail

PROJECT_DIR="$(cd "$(dirname "$0")/.." && pwd)"
HOST_FILE="$PROJECT_DIR/deploy/.ec2-host"
EC2_KEY="${EC2_KEY:-$HOME/Downloads/rp-key.pem}"
BUILD_DIR="${BUILD_DIR:-$(cd "$PROJECT_DIR/.." && pwd)/Build/rolypoly_build}"
STATUS_FILE="$PROJECT_DIR/Library/LinuxServerBuild/status.txt"

host=""
do_build=1
do_server=1
do_backend=1
for arg in "$@"; do
  case "$arg" in
    --skip-build) do_build=0 ;;
    --server-only) do_backend=0 ;;
    --backend-only) do_server=0; do_build=0 ;;
    -h|--help) sed -n 2,10p "$0"; exit 0 ;;
    -*) echo "알 수 없는 옵션: $arg" >&2; exit 1 ;;
    *) host="$arg" ;;
  esac
done

if [ -z "$host" ] && [ -f "$HOST_FILE" ]; then
  host="$(cat "$HOST_FILE")"
fi
if [ -z "$host" ]; then
  echo "EC2 공인 IP를 인자로 주세요. 예: deploy/deploy.sh 3.35.12.34" >&2
  exit 1
fi
echo "$host" > "$HOST_FILE"

step() { printf '\n\033[1;36m==> %s\033[0m\n' "$*"; }

# 같은 세션의 ssh/rsync가 연결 하나를 재사용한다.
SSH_OPTS=(-i "$EC2_KEY" -o StrictHostKeyChecking=accept-new -o ConnectTimeout=10 -o IPQoS=throughput
  -o ControlMaster=auto -o ControlPath="/tmp/rolypoly-deploy-%C" -o ControlPersist=120)
remote() { ssh "${SSH_OPTS[@]}" "ubuntu@$host" "$@"; }
sync_to() { rsync -az -e "ssh ${SSH_OPTS[*]}" "$@"; }

# macOS 27의 ssh는 수십 MB 이상을 한 연결로 보내면 "Result too large"로 끊긴다(scp·rsync 모두).
# 2MB 조각으로 나눠 조각마다 재시도하며 올린 뒤 EC2에서 합치고 SHA-256으로 검증한다.
upload_in_chunks() {
  local src="$1" dest="$2"
  local parts
  parts="$(mktemp -d)"
  split -b 2m "$src" "$parts/part."
  local total
  total="$(ls "$parts" | wc -l | tr -d ' ')"
  remote "rm -rf ~/.upload-$dest && mkdir ~/.upload-$dest"

  local n=0
  for part in "$parts"/part.*; do
    n=$((n + 1))
    local size name tries=0
    size="$(stat -f %z "$part")"
    name="$(basename "$part")"
    until remote "cat > ~/.upload-$dest/$name && stat -c %s ~/.upload-$dest/$name" < "$part" 2>/dev/null \
        | grep -qx "$size"; do
      tries=$((tries + 1))
      if [ "$tries" -ge 5 ]; then
        echo "조각 $name 업로드 실패" >&2
        exit 1
      fi
      sleep 1
    done
    printf '\r  %d/%d' "$n" "$total"
  done
  echo

  local sum
  sum="$(shasum -a 256 "$src" | cut -d' ' -f1)"
  remote "cat ~/.upload-$dest/part.* > ~/$dest && rm -rf ~/.upload-$dest && sha256sum ~/$dest" \
    | grep -q "^$sum " || { echo "업로드한 $dest 체크섬이 다릅니다." >&2; exit 1; }
  rm -rf "$parts"
}

build_server() {
  local output="$BUILD_DIR/server.x86_64"
  local editor_ready=0
  if unity status 2>/dev/null | grep -F "$PROJECT_DIR" | grep -q ready; then
    editor_ready=1
  fi

  if [ "$editor_ready" = 1 ]; then
    step "열려 있는 Unity 에디터에서 Linux 서버 빌드"
    rm -f "$STATUS_FILE"
    local queued
    queued="$(unity command --project-path "$PROJECT_DIR" eval --code"return LinuxServerBuild.BuildAsync(\"$output\");" 2>&1 || true)"
    if [ ! -f "$STATUS_FILE" ]; then
      echo "빌드를 시작하지 못했습니다(컴파일 에러?):" >&2
      echo "$queued" >&2
      exit 1
    fi
    local status=""
    for _ in $(seq 1 720); do
      status="$(head -1 "$STATUS_FILE" 2>/dev/null || true)"
      [ "$status" = "succeeded" ] || [ "$status" = "failed" ] && break
      sleep 5
    done
    sed -n 2p "$STATUS_FILE" 2>/dev/null || true
    if [ "$status" != "succeeded" ]; then
      echo "빌드가 끝나지 않았거나 실패했습니다." >&2
      exit 1
    fi
  else
    step "Unity 배치 모드로 Linux 서버 빌드(에디터가 꺼져 있음)"
    unity build "$PROJECT_DIR" --target StandaloneLinux64 \
      --execute-method LinuxServerBuild.BuildFromCommandLine -o "$output"
  fi
}

deploy_backend() {
  step "백엔드 업로드(node_modules, .env 제외)"
  local changes
  changes="$(sync_to --itemize-changes --exclude node_modules --exclude .env \
    "$PROJECT_DIR/backend/" "ubuntu@$host:backend/")"
  if [ -z "$changes" ]; then
    echo "바뀐 파일 없음 — 재시작 생략"
    return
  fi
  echo "$changes"
  if echo "$changes" | grep -qE 'package(-lock)?\.json'; then
    step "의존성 설치"
    remote 'cd ~/backend && npm ci --omit=dev'
  fi
  remote 'sudo systemctl restart br-backend'
}

deploy_server() {
  if [ ! -x "$BUILD_DIR/server.x86_64" ]; then
    echo "빌드 결과가 없습니다: $BUILD_DIR/server.x86_64" >&2
    exit 1
  fi

  # 새 폴더에 풀어 두고 재시작 직전에 바꿔 끼운다(직전 버전은 rolypoly-server.old로 남김).
  step "게임 서버 압축"
  local archive
  archive="$(mktemp -d)/server.tgz"
  COPYFILE_DISABLE=1 tar -C "$BUILD_DIR" --no-mac-metadata --no-xattrs --exclude '*_DoNotShip' -czf "$archive" .
  upload_in_chunks "$archive" server.tgz
  rm -rf "$(dirname "$archive")"
  remote 'set -e
    rm -rf ~/rolypoly-server.new
    mkdir ~/rolypoly-server.new
    tar -xzf ~/server.tgz -C ~/rolypoly-server.new
    rm ~/server.tgz'
  sync_to "$PROJECT_DIR/deploy/ec2/start-server.sh" "ubuntu@$host:rolypoly-server.new/"
  sync_to "$PROJECT_DIR/deploy/ec2/rolypoly-server.service" "ubuntu@$host:"

  step "게임 서버 재시작"
  # 예전처럼 nohup으로 띄운 서버가 남아 있으면 7777 포트가 겹치므로 함께 끈다.
  # [s]로 감싸 pkill이 이 원격 셸 자신을 죽이지 않게 한다.
  remote 'set -e
    sudo install -m 644 ~/rolypoly-server.service /etc/systemd/system/rolypoly-server.service
    rm ~/rolypoly-server.service
    sudo systemctl daemon-reload
    sudo systemctl enable --quiet rolypoly-server
    sudo systemctl stop rolypoly-server || true
    pkill -f "[s]erver.x86_64" || true
    sleep 1
    rm -rf ~/rolypoly-server.old
    if [ -d ~/rolypoly-server ]; then mv ~/rolypoly-server ~/rolypoly-server.old; fi
    mv ~/rolypoly-server.new ~/rolypoly-server
    sudo systemctl start rolypoly-server'
}

check() {
  step "상태 확인"
  sleep 5
  # 서비스가 하나라도 active가 아니면 실패로 끝낸다.
  if ! remote 'ok=1
    for svc in br-backend rolypoly-server; do
      state="$(systemctl is-active "$svc" || true)"
      printf "%s: %s\n" "$svc" "$state"
      [ "$state" = active ] || ok=0
    done
    curl -s -m 3 localhost:3000/health && echo
    grep -a -v "permitted lifetime" ~/gameserver.log | grep -a -E "\[Dedicated|\[start-server|Exception" | tail -5
    [ "$ok" = 1 ]'; then
    echo "서비스가 정상 실행 중이 아닙니다. EC2에서 journalctl -u rolypoly-server 또는 ~/gameserver.log를 확인하세요." >&2
    exit 1
  fi
}

if [ "$do_server" = 1 ] && [ "$do_build" = 1 ]; then build_server; fi
if [ "$do_backend" = 1 ]; then deploy_backend; fi
if [ "$do_server" = 1 ]; then deploy_server; fi
check
echo
echo "배포 완료: $host (게임 $host:7777, 백엔드 http://$host:3000)"
