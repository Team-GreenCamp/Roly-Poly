#!/usr/bin/env bash
# EC2에서 게임 서버를 띄운다(rolypoly-server.service가 실행).
# 공인 IP는 Learner Lab 세션마다 바뀌므로 매번 인스턴스 메타데이터에서 읽어 방 목록에 등록한다.
# 설정을 바꾸려면 ~/rolypoly-server.env에 아래 변수를 적는다.
set -euo pipefail

PORT="${PORT:-7777}"
SCENE="${SCENE:-Survival Arena}"
ROOM_NAME="${ROOM_NAME:-AWS 서버}"
API_URL="${API_URL:-http://127.0.0.1:3000}"

if [ -z "${PUBLIC_IP:-}" ]; then
  token=$(curl -sf -m 2 -X PUT http://169.254.169.254/latest/api/token \
    -H "X-aws-ec2-metadata-token-ttl-seconds: 60" || true)
  PUBLIC_IP=$(curl -sf -m 2 -H "X-aws-ec2-metadata-token: ${token}" \
    http://169.254.169.254/latest/meta-data/public-ipv4 || true)
fi

args=(-batchmode -nographics -dedicatedServer -port "$PORT" -scene "$SCENE")
if [ -n "$PUBLIC_IP" ]; then
  args+=(-apiUrl "$API_URL" -publicIp "$PUBLIC_IP" -roomName "$ROOM_NAME")
else
  echo "[start-server] 공인 IP를 알 수 없어 방 목록 등록 없이 시작합니다." >&2
fi

cd "$(dirname "$0")"
exec ./server.x86_64 "${args[@]}"
