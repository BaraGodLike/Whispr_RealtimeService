# Whispr Realtime Service

[English version](README.md)

Whispr Realtime Service — это внешняя realtime-точка входа для клиентов Whispr.

Сервис принимает WebSocket-соединения от клиентов, аутентифицирует их через `Mailbox Service`, хранит активные lease `mailbox -> connection` в Redis, проксирует исходящие сообщения в `Relay`, читает события `message.enqueued` из Kafka и доставляет payload online, если mailbox в данный момент подключен к этому instance.

Основное поведение:
- клиент открывает WebSocket-соединение с `Realtime`;
- первая прикладная команда — `authenticate`;
- `Realtime` вызывает `Mailbox.CompleteRealtimeAuth`;
- при успехе он получает активные mailbox пользователя и регистрирует их в Redis;
- `Realtime` принимает от клиента команды `send_message`, `ack`, `ack_batch` и `resume`;
- `send_message` проксируется в `Relay.EnqueueMessage`;
- Kafka consumer получает события `message.enqueued`;
- если lease destination mailbox указывает на этот node, `Realtime` забирает payload через `Relay.GetMessage` и отправляет его клиенту по WebSocket;
- при reconnect клиент может вызвать `resume`, а `Realtime` запросит pending-сообщения через `Relay.GetPendingMessages`.

Realtime не делает:
- разрешение mailbox ownership без `Mailbox Service`;
- самостоятельную проверку Ed25519-подписи;
- хранение payload;
- хранение outbox;
- публикацию событий в Kafka;
- расшифровку payload;
- push-уведомления.

## Возможности

- WebSocket-based entry point для realtime-клиента.
- JSON application protocol для auth, send, ack, batch ack и resume.
- Аутентификация mailbox полностью делегирована `Mailbox Service`.
- Redis-backed registry lease-ов mailbox между instance-ами.
- Last-write-wins замена соединений.
- Kafka fan-out consumption через отдельный consumer group id на каждый instance.
- Online delivery payload через `Relay.GetMessage`.
- Resume flow через `Relay.GetPendingMessages`.
- Структурированные single-line JSON-логи с metadata о сервисе и instance.
- HTTP health endpoint.

## Структура solution

- `Services` - ASP.NET Core host, WebSocket endpoint, connection registry, фоновый maintenance loop.
- `Application` - use cases, orchestration contracts, options, абстракция log scope.
- `Domain` - базовые модели соединений и сообщений.
- `Contracts` - DTO и message type constants для WebSocket JSON protocol.
- `Infrastructure.Redis` - хранение lease-ов и connection index в Redis.
- `Infrastructure.Grpc` - локальные proto-файлы и gRPC clients для `Mailbox` и `Relay`.
- `Infrastructure.Messaging` - Kafka consumer для `message.enqueued`.
- `Domain.Tests` - domain unit tests.
- `Application.Tests` - application unit tests.

## Runtime state

Realtime хранит только эфемерное состояние.

Общее состояние в Redis:
- `conn:{mailbox} -> "{node_id}:{conn_id}"`
- `connidx:{conn_id} -> { nodeId, mailboxes[] }`

Локальное in-memory состояние на host:
- `conn_id -> socket`
- `conn_id -> authenticated flag`
- `conn_id -> user_id`
- `conn_id -> mailboxes[]`
- временные метки соединения для auth timeout и lease refresh

Отдельной Postgres БД для Realtime в v1 нет.

## Authentication flow

Realtime не проверяет подпись самостоятельно.

Ожидаемый flow:
1. клиент получает nonce через `Mailbox.BeginRealtimeAuth`;
2. клиент подписывает challenge;
3. клиент открывает WebSocket к `Realtime`;
4. клиент отправляет `authenticate`;
5. `Realtime` вызывает `Mailbox.CompleteRealtimeAuth`;
6. `Mailbox` валидирует nonce и подпись и возвращает активные mailbox;
7. `Realtime` сохраняет mailbox lease-ы в Redis и помечает соединение authenticated.

Если аутентификация в Mailbox завершается ошибкой, Realtime возвращает `forbidden`.

## WebSocket API

Локальный WebSocket endpoint по умолчанию в Docker Compose: `ws://localhost:${REALTIME_SERVICE_PORT}/ws`, где значение по умолчанию из `.env` — `8080`.

Health endpoint:
- `GET http://localhost:${REALTIME_SERVICE_PORT}/`

Все клиентские сообщения отправляются как text JSON в таком envelope:

```json
{
  "type": "authenticate",
  "data": {}
}
```

Поддерживаемые клиентские message types:
- `authenticate`
- `send_message`
- `ack`
- `ack_batch`
- `resume`

Поддерживаемые серверные message types:
- `authenticated`
- `send_message_accepted`
- `ack_accepted`
- `ack_batch_accepted`
- `incoming_message`
- `resume_messages`
- `error`

Файлы с контрактом протокола:
- [Contracts/Protocol/Messages.cs](Contracts/Protocol/Messages.cs)
- [Contracts/Protocol/RealtimeMessageTypes.cs](Contracts/Protocol/RealtimeMessageTypes.cs)

### Authenticate

Пример запроса:

```json
{
  "type": "authenticate",
  "data": {
    "userId": "user-123",
    "nonce": "nonce-from-mailbox",
    "alg": "ed25519",
    "signature": "BASE64_SIGNATURE"
  }
}
```

Пример ответа:

```json
{
  "type": "authenticated",
  "data": {
    "success": true,
    "registeredMailboxCount": 6
  }
}
```

Правила:
- это должна быть первая прикладная команда;
- на одно соединение поддерживается только одна аутентификация;
- неаутентифицированное соединение истекает по таймауту через `15` секунд по умолчанию.

### SendMessage

Пример запроса:

```json
{
  "type": "send_message",
  "data": {
    "msgId": "11111111-2222-3333-4444-555555555555",
    "destMailbox": "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
    "payload": "AQIDBA=="
  }
}
```

Пример ответа:

```json
{
  "type": "send_message_accepted",
  "data": {
    "accepted": true
  }
}
```

Примечания:
- `payload` — это opaque encrypted envelope в base64;
- максимальный размер payload — `256 KB`;
- Realtime пересылает запрос в `Relay.EnqueueMessage`.

### Ack

Пример запроса:

```json
{
  "type": "ack",
  "data": {
    "msgId": "11111111-2222-3333-4444-555555555555"
  }
}
```

Пример ответа:

```json
{
  "type": "ack_accepted",
  "data": {
    "success": true
  }
}
```

### AckBatch

Пример запроса:

```json
{
  "type": "ack_batch",
  "data": {
    "msgIds": [
      "11111111-2222-3333-4444-555555555555",
      "66666666-7777-8888-9999-000000000000"
    ]
  }
}
```

Пример ответа:

```json
{
  "type": "ack_batch_accepted",
  "data": {
    "ackedCount": 2
  }
}
```

### Resume

Пример запроса:

```json
{
  "type": "resume",
  "data": {
    "limit": 100
  }
}
```

Пример ответа:

```json
{
  "type": "resume_messages",
  "data": {
    "messages": [
      {
        "msgId": "11111111-2222-3333-4444-555555555555",
        "payload": "AQIDBA=="
      }
    ],
    "hasMore": false
  }
}
```

Правила:
- допустимый диапазон — `1..500`;
- resume полностью инициируется клиентом;
- пагинация строится на повторных запросах, без cursor.

### IncomingMessage

Пример server push:

```json
{
  "type": "incoming_message",
  "data": {
    "msgId": "11111111-2222-3333-4444-555555555555",
    "payload": "AQIDBA=="
  }
}
```

### Error

Пример:

```json
{
  "type": "error",
  "data": {
    "code": "invalid_request",
    "message": "Request validation failed."
  }
}
```

Текущие `code`:
- `invalid_request`
- `unauthenticated`
- `forbidden`
- `upstream_unavailable`
- `internal_error`

Примечания:
- upstream `InvalidArgument` и `AlreadyExists` от gRPC-зависимостей возвращаются как `invalid_request`;
- прочие транспортные и dependency-level gRPC ошибки возвращаются как `upstream_unavailable`.

## gRPC dependencies

Realtime работает только как gRPC client.

Зависимость Mailbox:
- локальный proto-файл: [Infrastructure.Grpc/Protos/mailbox.proto](Infrastructure.Grpc/Protos/mailbox.proto)
- сейчас используется RPC: `CompleteRealtimeAuth`

Зависимость Relay:
- локальный proto-файл: [Infrastructure.Grpc/Protos/relay_service.proto](Infrastructure.Grpc/Protos/relay_service.proto)
- используются RPC:
  - `EnqueueMessage`
  - `GetMessage`
  - `GetPendingMessages`
  - `AckMessage`
  - `AckMessagesBatch`

## Kafka consumption

Realtime читает только lightweight enqueue event, а не сам message payload.

Контракт события:
- [Infrastructure.Messaging/Protos/relay_events.proto](Infrastructure.Messaging/Protos/relay_events.proto)

Topic:
- `message.enqueued`

Payload события:
- `msg_id`
- `dest_mailbox`

Модель consumption:
- каждый instance Realtime использует собственный consumer group id, собранный из `Kafka:GroupIdPrefix` и `Realtime:NodeId`;
- поэтому каждый instance независимо получает все enqueue events;
- каждый instance проверяет Redis и доставляет только те сообщения, чей mailbox lease указывает на него самого.

Поведение доставки:
1. получить `message.enqueued`;
2. прочитать `conn:{dest_mailbox}` из Redis;
3. пропустить, если mailbox offline;
4. пропустить, если lease принадлежит другому node;
5. вызвать `Relay.GetMessage(msg_id)`;
6. отправить `incoming_message` в локальное WebSocket-соединение.

Примечания:
- дубликаты Kafka events допустимы и ожидаемы;
- если `Relay.GetMessage` возвращает `NOT_FOUND`, это считается benign race и логируется как warning;
- при временных ошибках получения payload Realtime делает in-memory retry и не коммитит Kafka offset, если retry исчерпаны.

## Lifecycle соединения

Значения по умолчанию:
- Redis lease TTL: `60 seconds`
- lease refresh interval: `25 seconds`
- heartbeat maintenance interval: `10 seconds`
- authentication timeout: `15 seconds`

Поведение:
- authenticated-соединения периодически refresh-ят Redis lease;
- если Redis lease заменен другим соединением, старое соединение закрывается;
- при disconnect cleanup удаляет локальное connection state и Redis mailbox lease-ы в best effort режиме;
- команды, отправленные до authentication, получают `unauthenticated`, а соединение закрывается.

## Health

Сейчас Realtime публикует HTTP health endpoint:
- `GET /`

Текущее поведение health:
- возвращает `200 OK` и `{ "status": "ok" }`
- стандартный gRPC health service в текущей реализации не поднимается

## Логирование

- `Realtime Service` пишет структурированные single-line JSON-логи в stdout.
- Каждая .NET log entry содержит стандартные поля JSON console formatter, например:
  - `EventId`
  - `LogLevel`
  - `Category`
  - `Message`
  - `State`
- Scope enrichment добавляет:
  - `service`
  - `instance`
- Service metadata берется из:
  - `Realtime:ServiceName`
  - `Realtime:NodeId`
- Если `Realtime:NodeId` задан как `auto` или пустой, в контейнерной среде используется hostname контейнера.
- По design intent логи не должны содержать:
  - `userId`
  - mailbox id
  - message id
  - payload bytes
  - signatures
  - nonces
- Допускается логировать sanitised technical metadata, например `ExceptionType` и gRPC status code.

Текущие правила уровней:
- `Default = Warning`
- `Microsoft = Warning`
- `Microsoft.Hosting.Lifetime = Information`
- `Application = Information`
- `Realtime.Services = Information`
- `Infrastructure.Redis = Warning`
- `Infrastructure.Grpc = Warning`
- `Infrastructure.Messaging = Warning`

## Запуск через Docker Compose

Текущий Docker Compose поднимает:
- `realtime-service`
- Redis
- RedisInsight

Важно:
- Kafka ожидается за пределами этого Compose stack;
- `Mailbox Service` ожидается за пределами этого Compose stack;
- `Relay Service` ожидается за пределами этого Compose stack;
- Compose прокидывает адреса этих зависимостей через environment variables и `host.docker.internal`.

### 1. Подготовь `.env`

Скопируй [.env.example](.env.example) в `.env` и заполни нужные значения:
- `REALTIME_SERVICE_PORT`
- `REALTIME_NODE_ID`
- `REALTIME_SERVICE_NAME`
- `REDIS_PORT`
- `REDIS_INSIGHT_PORT`
- `KAFKA_TOPIC`
- `KAFKA_GROUP_ID_PREFIX`
- `KAFKA_BOOTSTRAP_SERVERS`
- `MAILBOX_GRPC_ADDRESS`
- `RELAY_GRPC_ADDRESS`

### 2. Подними стек

```powershell
docker compose up --build
```

Локальные endpoint по умолчанию после старта:
- WebSocket: `ws://localhost:${REALTIME_SERVICE_PORT}/ws`, по умолчанию `8080`
- HTTP health: `http://localhost:${REALTIME_SERVICE_PORT}/`
- RedisInsight: `http://localhost:${REDIS_INSIGHT_PORT}`, по умолчанию `5541`

### 3. Перезапусти только `realtime-service`

Если Redis уже запущен:

```powershell
docker compose up --no-deps --build realtime-service
```

## Локальный запуск без Docker

### Services

```powershell
dotnet run --project Services
```

Локальные launch settings лежат в [Services/Properties/launchSettings.json](Services/Properties/launchSettings.json).

По умолчанию `dotnet run` использует:
- `http://localhost:5291`
- `https://localhost:7162`

## Тестирование API

WebSocket protocol удобно тестировать через Postman или любой другой WebSocket client.

Для Postman:
1. Создай `WebSocket Request`.
2. Подключись к `ws://localhost:${REALTIME_SERVICE_PORT}/ws`.
3. Отправляй text JSON messages в envelope, описанном выше.
4. Перед `send_message`, `ack`, `ack_batch` и `resume` нужно пройти authentication.

Важно:
- для валидной authentication нужен реальный nonce из `Mailbox.BeginRealtimeAuth` и корректная подпись;
- `payload` и `signature` должны быть base64-строками.

## Технологический стек

- .NET 10
- ASP.NET Core
- WebSockets
- gRPC client integrations
- Redis
- Kafka
- MSTest
