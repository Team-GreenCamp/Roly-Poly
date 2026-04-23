# Game Room Backend

Node.js + Express + MySQL 최소 스캐폴딩입니다.

## 실행 준비

1. `backend/.env.example`을 복사해 `.env`를 만듭니다.
2. `backend/schema.sql`을 MySQL에 적용합니다. 기본 DB 이름은 `game`입니다.
3. `backend/`에서 의존성을 설치한 뒤 서버를 실행합니다.

`.env`에는 실제 MySQL 비밀번호가 들어가므로 Git에 올리지 않습니다.

## 설치

```bash
cd backend
npm install
```

## 실행

```bash
npm run dev
```

기본 포트는 `3000`입니다.

## DB 적용

```bash
set -a
source .env
set +a
mysql -u"$MYSQL_USER" -p"$MYSQL_PASSWORD" -h "$MYSQL_HOST" -P "$MYSQL_PORT" --execute "source schema.sql"
```

## 로컬 확인

```bash
curl -s http://localhost:3000/health
curl -s http://localhost:3000/rooms
```

## API

- `GET /health`
- `GET /rooms`
- `POST /rooms`
- `POST /rooms/:id/join`
- `POST /rooms/:id/leave`
- `PATCH /rooms/:id`

## Unity 연동 흐름

1. Unity 로비에서 호스트를 시작합니다.
2. Unity가 `POST /rooms`로 방 이름과 `connectionType`, `connectionValue`를 등록합니다.
3. 다른 클라이언트는 `GET /rooms`로 공개 방 목록을 표시합니다.
4. 방 선택 시 `POST /rooms/:id/join`으로 정원과 상태를 확인합니다.
5. 응답의 `connectionType`, `connectionValue`로 Unity Netcode 접속을 시작합니다.

## 요청 예시

### POST /rooms

```json
{
  "name": "Room A",
  "connectionType": "local",
  "connectionValue": "127.0.0.1:7777",
  "mapId": "tutorial",
  "isPublic": true,
  "maxPlayers": 4,
  "currentPlayers": 1
}
```

### PATCH /rooms/:id

```json
{
  "name": "Room B",
  "connectionType": "local",
  "connectionValue": "127.0.0.1:7777",
  "mapId": "factory",
  "isPublic": true,
  "maxPlayers": 8,
  "status": "open"
}
```

`connectionType`은 로컬 개발에서는 `local`, Unity Relay를 붙일 때는 `relay`로 사용합니다.

## 현재 Unity 기본값

Unity 로비는 기본적으로 로컬 모드로 방 목록을 사용합니다.

- `connectionType`: `local`
- `connectionValue`: `127.0.0.1:7777`
- `backendBaseUrl`: `http://localhost:3000`

추후 Unity Relay로 전환할 때는 `LobbyUIController`의 `useRelayForRoomList`를 켜면 Relay Join Code를 방 목록에 등록하는 흐름을 사용합니다.
