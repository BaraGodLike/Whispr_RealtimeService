# Whispr Realtime Service

[Русская версия](README.ru.md)

Whispr Realtime Service is the external realtime entry point for Whispr clients.

It accepts WebSocket connections from clients, authenticates them through `Mailbox Service`, tracks active mailbox-to-connection leases in Redis, forwards outbound messages to `Relay`, consumes `message.enqueued` events from Kafka, and delivers message payloads online when the destination mailbox is currently connected to this instance.

Core behavior:
- a client opens a WebSocket connection to `Realtime`;
- the first application command is `authenticate`;
- `Realtime` calls `Mailbox.CompleteRealtimeAuth`;
- on success it receives the user's active mailboxes and registers them in Redis;
- `Realtime` accepts `send_message`, `ack`, `ack_batch`, and `resume` commands from the client;
- `send_message` is forwarded to `Relay.EnqueueMessage`;
- a Kafka consumer receives `message.enqueued` events;
- if the destination mailbox lease points to this node, `Realtime` fetches the payload from `Relay.GetMessage` and pushes it to the client over WebSocket;
- on reconnect, the client can call `resume`, and `Realtime` fetches pending messages from `Relay.GetPendingMessages`.

Realtime does not do:
- mailbox ownership resolution without `Mailbox Service`;
- Ed25519 signature verification on its own;
- payload storage;
- outbox storage;
- Kafka event publication;
- payload decryption;
- push notifications.

## Features

- WebSocket-based realtime client entry point.
- JSON application protocol for auth, send, ack, batch ack, and resume.
- Mailbox authentication delegated to `Mailbox Service`.
- Redis-backed mailbox lease registry shared across instances.
- Last-write-wins connection replacement.
- Kafka fan-out consumption with per-instance consumer group ids.
- Online payload delivery via `Relay.GetMessage`.
- Resume flow via `Relay.GetPendingMessages`.
- Structured single-line JSON logs with service and instance metadata.
- HTTP health endpoint.

## Solution structure

- `Services` - ASP.NET Core host, WebSocket endpoint, connection registry, background maintenance loop.
- `Application` - use cases, orchestration contracts, options, logging scope abstraction.
- `Domain` - core connection and message models.
- `Contracts` - WebSocket JSON protocol DTOs and message type constants.
- `Infrastructure.Redis` - Redis lease and connection index storage.
- `Infrastructure.Grpc` - local proto files and gRPC clients for `Mailbox` and `Relay`.
- `Infrastructure.Messaging` - Kafka consumer for `message.enqueued`.
- `Domain.Tests` - domain unit tests.
- `Application.Tests` - application unit tests.

## Runtime state

Realtime keeps only ephemeral state.

Shared state in Redis:
- `conn:{mailbox} -> "{node_id}:{conn_id}"`
- `connidx:{conn_id} -> { nodeId, mailboxes[] }`

Local in-memory state per host:
- `conn_id -> socket`
- `conn_id -> authenticated flag`
- `conn_id -> user_id`
- `conn_id -> mailboxes[]`
- connection timestamps used for auth timeout and lease refresh

There is no dedicated Postgres database for Realtime in v1.

## Authentication flow

Realtime does not verify the signature by itself.

Expected flow:
1. the client obtains a nonce from `Mailbox.BeginRealtimeAuth`;
2. the client signs the challenge payload;
3. the client opens `Realtime` WebSocket;
4. the client sends `authenticate`;
5. `Realtime` calls `Mailbox.CompleteRealtimeAuth`;
6. `Mailbox` validates the nonce and signature and returns active mailboxes;
7. `Realtime` stores mailbox leases in Redis and marks the connection authenticated.

If authentication fails in Mailbox, Realtime returns `forbidden`.

## WebSocket API

Default WebSocket endpoint in Docker Compose: `ws://localhost:${REALTIME_SERVICE_PORT}/ws` with `8080` as the default from `.env`.

Health endpoint:
- `GET http://localhost:${REALTIME_SERVICE_PORT}/`

All client messages are sent as text JSON in this envelope:

```json
{
  "type": "authenticate",
  "data": {}
}
```

Supported client message types:
- `authenticate`
- `send_message`
- `ack`
- `ack_batch`
- `resume`

Supported server message types:
- `authenticated`
- `send_message_accepted`
- `ack_accepted`
- `ack_batch_accepted`
- `incoming_message`
- `resume_messages`
- `error`

Protocol source files:
- [Contracts/Protocol/Messages.cs](Contracts/Protocol/Messages.cs)
- [Contracts/Protocol/RealtimeMessageTypes.cs](Contracts/Protocol/RealtimeMessageTypes.cs)

### Authenticate

Request example:

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

Response example:

```json
{
  "type": "authenticated",
  "data": {
    "success": true,
    "registeredMailboxCount": 6
  }
}
```

Rules:
- this must be the first application command;
- only one authentication attempt per connection is supported;
- unauthenticated connections time out after `15` seconds by default.

### SendMessage

Request example:

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

Response example:

```json
{
  "type": "send_message_accepted",
  "data": {
    "accepted": true
  }
}
```

Notes:
- `payload` is an opaque encrypted envelope encoded as base64;
- maximum payload size is `256 KB`;
- Realtime forwards the request to `Relay.EnqueueMessage`.

### Ack

Request example:

```json
{
  "type": "ack",
  "data": {
    "msgId": "11111111-2222-3333-4444-555555555555"
  }
}
```

Response example:

```json
{
  "type": "ack_accepted",
  "data": {
    "success": true
  }
}
```

### AckBatch

Request example:

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

Response example:

```json
{
  "type": "ack_batch_accepted",
  "data": {
    "ackedCount": 2
  }
}
```

### Resume

Request example:

```json
{
  "type": "resume",
  "data": {
    "limit": 100
  }
}
```

Response example:

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

Rules:
- valid range is `1..500`;
- resume is explicitly client-driven;
- pagination is repeated-request based, not cursor-based.

### IncomingMessage

Server push example:

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

Example:

```json
{
  "type": "error",
  "data": {
    "code": "invalid_request",
    "message": "Request validation failed."
  }
}
```

Current error codes:
- `invalid_request`
- `unauthenticated`
- `forbidden`
- `upstream_unavailable`
- `internal_error`

Notes:
- upstream `InvalidArgument` and `AlreadyExists` from gRPC dependencies are surfaced as `invalid_request`;
- other gRPC transport and dependency failures are surfaced as `upstream_unavailable`.

## gRPC dependencies

Realtime acts only as a gRPC client.

Mailbox dependency:
- local proto file: [Infrastructure.Grpc/Protos/mailbox.proto](Infrastructure.Grpc/Protos/mailbox.proto)
- currently used RPC: `CompleteRealtimeAuth`

Relay dependency:
- local proto file: [Infrastructure.Grpc/Protos/relay_service.proto](Infrastructure.Grpc/Protos/relay_service.proto)
- used RPCs:
  - `EnqueueMessage`
  - `GetMessage`
  - `GetPendingMessages`
  - `AckMessage`
  - `AckMessagesBatch`

## Kafka consumption

Realtime consumes only the lightweight enqueue event, not the message payload.

Event contract:
- [Infrastructure.Messaging/Protos/relay_events.proto](Infrastructure.Messaging/Protos/relay_events.proto)

Topic:
- `message.enqueued`

Event payload:
- `msg_id`
- `dest_mailbox`

Consumption model:
- every Realtime instance uses its own consumer group id built from `Kafka:GroupIdPrefix` and `Realtime:NodeId`;
- every instance therefore consumes all enqueue events independently;
- each instance checks Redis and only delivers messages for mailbox leases pointing to itself.

Delivery behavior:
1. receive `message.enqueued`;
2. read `conn:{dest_mailbox}` from Redis;
3. skip if the mailbox is offline;
4. skip if the lease belongs to another node;
5. call `Relay.GetMessage(msg_id)`;
6. push `incoming_message` to the local WebSocket connection.

Notes:
- duplicate Kafka events are allowed and expected;
- `Relay.GetMessage` returning `NOT_FOUND` is treated as a benign race and logged as a warning;
- on temporary fetch failures, Realtime retries in memory and does not commit the Kafka offset if retries are exhausted.

## Connection lifecycle

Defaults:
- Redis lease TTL: `60 seconds`
- lease refresh interval: `25 seconds`
- heartbeat maintenance interval: `10 seconds`
- authentication timeout: `15 seconds`

Behavior:
- authenticated connections refresh their Redis lease periodically;
- if a Redis lease is replaced by another connection, the old connection is closed;
- disconnect cleanup removes local connection state and Redis mailbox leases on best effort;
- commands sent before authentication return `unauthenticated` and the connection is closed.

## Health

Realtime currently exposes an HTTP health endpoint:
- `GET /`

Current health behavior:
- returns `200 OK` with `{ "status": "ok" }`
- no standard gRPC health service is exposed in the current implementation

## Logging

- `Realtime Service` writes structured single-line JSON logs to stdout.
- Every .NET log entry includes the standard JSON console fields such as:
  - `EventId`
  - `LogLevel`
  - `Category`
  - `Message`
  - `State`
- Service scope enrichment adds:
  - `service`
  - `instance`
- Service metadata comes from:
  - `Realtime:ServiceName`
  - `Realtime:NodeId`
- If `Realtime:NodeId` is set to `auto` or left empty in containerized environments, the container host name is used.
- Logs are intentionally designed not to include:
  - `userId`
  - mailbox ids
  - message ids
  - payload bytes
  - signatures
  - nonces
- Sanitized technical metadata such as `ExceptionType` and gRPC status codes may be logged.

Current log level rules:
- `Default = Warning`
- `Microsoft = Warning`
- `Microsoft.Hosting.Lifetime = Information`
- `Application = Information`
- `Realtime.Services = Information`
- `Infrastructure.Redis = Warning`
- `Infrastructure.Grpc = Warning`
- `Infrastructure.Messaging = Warning`

## Running with Docker Compose

The current Docker Compose setup includes:
- `realtime-service`
- Redis
- RedisInsight

Important:
- Kafka is expected to run outside this Compose stack;
- `Mailbox Service` is expected to run outside this Compose stack;
- `Relay Service` is expected to run outside this Compose stack;
- Compose wires those dependencies through environment variables and `host.docker.internal`.

### 1. Prepare `.env`

Copy [.env.example](.env.example) to `.env` and fill the required values:
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

### 2. Start the stack

```powershell
docker compose up --build
```

Default local endpoints after startup:
- WebSocket: `ws://localhost:${REALTIME_SERVICE_PORT}/ws` with `8080` as the default
- HTTP health: `http://localhost:${REALTIME_SERVICE_PORT}/`
- RedisInsight: `http://localhost:${REDIS_INSIGHT_PORT}` with `5541` as the default

### 3. Restart only `realtime-service`

If Redis is already running:

```powershell
docker compose up --no-deps --build realtime-service
```

## Running locally without Docker

### Services

```powershell
dotnet run --project Services
```

Local launch settings are defined in [Services/Properties/launchSettings.json](Services/Properties/launchSettings.json).

By default, `dotnet run` uses:
- `http://localhost:5291`
- `https://localhost:7162`

## Testing the API

You can test the WebSocket protocol with Postman or any WebSocket client.

For Postman:
1. Create a `WebSocket Request`.
2. Connect to `ws://localhost:${REALTIME_SERVICE_PORT}/ws`.
3. Send JSON text messages using the envelope described above.
4. Authenticate before sending `send_message`, `ack`, `ack_batch`, or `resume`.

Important:
- valid authentication requires a real nonce from `Mailbox.BeginRealtimeAuth` and a valid signature;
- `payload` and `signature` must be base64 strings.

## Technology stack

- .NET 10
- ASP.NET Core
- WebSockets
- gRPC client integrations
- Redis
- Kafka
- MSTest
