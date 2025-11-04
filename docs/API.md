# API Documentation - C2 Server

Documentação completa da API REST do servidor C2.

## Base URL

```
http://localhost:8000
```

## Endpoints

### 1. Server Status

**GET** `/`

Retorna status e informações do servidor.

**Response:**
```json
{
  "status": "online",
  "service": "C2 Server",
  "version": "1.0.0",
  "agents_online": 2,
  "timestamp": "2025-11-03T10:30:00.123456"
}
```

---

### 2. List Agents

**GET** `/agents`

Lista todos os agentes conectados.

**Response:**
```json
{
  "agents": [
    {
      "agent_id": "desktop-win11-abc123",
      "hostname": "DESKTOP-WIN11",
      "connected_at": "2025-11-03T10:00:00Z",
      "last_seen": "2025-11-03T10:30:00Z",
      "status": "online"
    }
  ],
  "count": 1
}
```

**Agent Status:**
- `online` - Agente conectado e ativo (last_seen < 60s)
- `offline` - Agente inativo (last_seen > 60s)
- `disconnected` - Agente desconectado

---

### 3. Agent SSE Stream

**GET** `/agent/stream/{agent_id}?hostname={hostname}`

Endpoint SSE para agentes se conectarem e receberem comandos.

**Parameters:**
- `agent_id` (path) - ID único do agente
- `hostname` (query) - Nome do computador

**Headers:**
```
Accept: text/event-stream
Cache-Control: no-cache
```

**Response:** (Stream contínuo)

```
HTTP/1.1 200 OK
Content-Type: text/event-stream
Cache-Control: no-cache
Connection: keep-alive

: heartbeat

event: command
data: {"task_id": "550e8400-e29b-41d4-a716-446655440000", "command": "whoami"}

: heartbeat

```

**Eventos:**
- `: heartbeat` - Comentário enviado a cada 1s para manter conexão
- `event: command` - Comando para executar

---

### 4. Send Command

**POST** `/command`

Envia comando para um agente específico.

**Request Body:**
```json
{
  "agent_id": "desktop-win11-abc123",
  "command": "whoami"
}
```

**Response:**
```json
{
  "task_id": "550e8400-e29b-41d4-a716-446655440000",
  "agent_id": "desktop-win11-abc123",
  "command": "whoami",
  "status": "queued"
}
```

**Status Codes:**
- `200` - Comando enfileirado com sucesso
- `404` - Agente não encontrado
- `400` - Agente não está online

---

### 5. Submit Result

**POST** `/result`

Agente envia resultado de execução de comando.

**Request Body:**
```json
{
  "task_id": "550e8400-e29b-41d4-a716-446655440000",
  "agent_id": "desktop-win11-abc123",
  "output": "desktop-win11\user",
  "error": null,
  "exit_code": 0,
  "timestamp": "2025-11-03T10:30:00Z"
}
```

**Response:**
```json
{
  "status": "success",
  "message": "Result received"
}
```

**Status Codes:**
- `200` - Resultado recebido
- `404` - Task ID não encontrado

---

### 6. Get Result

**GET** `/result/{task_id}`

Consulta resultado de uma tarefa específica.

**Parameters:**
- `task_id` (path) - ID da tarefa

**Response:**
```json
{
  "task_id": "550e8400-e29b-41d4-a716-446655440000",
  "agent_id": "desktop-win11-abc123",
  "command": "whoami",
  "output": "desktop-win11\user",
  "error": null,
  "exit_code": 0,
  "timestamp": "2025-11-03T10:30:00Z",
  "status": "completed"
}
```

**Result Status:**
- `queued` - Comando enviado, aguardando execução
- `completed` - Comando executado com sucesso
- `failed` - Comando falhou na execução

**Status Codes:**
- `200` - Resultado encontrado
- `404` - Resultado não encontrado

---

### 7. List Results

**GET** `/results?limit={limit}`

Lista resultados recentes.

**Parameters:**
- `limit` (query, optional) - Número máximo de resultados (padrão: 50)

**Response:**
```json
{
  "results": [
    {
      "task_id": "550e8400-e29b-41d4-a716-446655440000",
      "agent_id": "desktop-win11-abc123",
      "command": "whoami",
      "output": "desktop-win11\user",
      "error": null,
      "exit_code": 0,
      "timestamp": "2025-11-03T10:30:00Z",
      "status": "completed"
    }
  ],
  "count": 1
}
```

---

### 8. Remove Agent

**DELETE** `/agent/{agent_id}`

Remove agente da lista ativa (endpoint administrativo).

**Parameters:**
- `agent_id` (path) - ID do agente a remover

**Response:**
```json
{
  "status": "success",
  "message": "Agent desktop-win11-abc123 removed"
}
```

**Status Codes:**
- `200` - Agente removido
- `404` - Agente não encontrado

---

## CORS

O servidor está configurado para aceitar requisições de:
- `http://localhost:4200`
- `http://127.0.0.1:4200`

Para adicionar outras origens, edite `server.py`:

```python
app.add_middleware(
    CORSMiddleware,
    allow_origins=["http://localhost:4200", "http://example.com"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)
```

---

## Error Responses

Todos os endpoints podem retornar erros no seguinte formato:

```json
{
  "detail": "Error message"
}
```

**Common Status Codes:**
- `400` - Bad Request
- `404` - Not Found
- `500` - Internal Server Error

---

## Examples

### cURL

**List agents:**
```bash
curl http://localhost:8000/agents
```

**Send command:**
```bash
curl -X POST http://localhost:8000/command \
  -H "Content-Type: application/json" \
  -d '{"agent_id": "desktop-abc", "command": "whoami"}'
```

**Get result:**
```bash
curl http://localhost:8000/result/550e8400-e29b-41d4-a716-446655440000
```

### JavaScript (Fetch API)

```javascript
// List agents
const response = await fetch('http://localhost:8000/agents');
const data = await response.json();
console.log(data.agents);

// Send command
const cmdResponse = await fetch('http://localhost:8000/command', {
  method: 'POST',
  headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify({
    agent_id: 'desktop-abc',
    command: 'whoami'
  })
});
const cmdData = await cmdResponse.json();
console.log(cmdData.task_id);
```

### Python (requests)

```python
import requests

# List agents
response = requests.get('http://localhost:8000/agents')
agents = response.json()['agents']

# Send command
cmd_response = requests.post(
    'http://localhost:8000/command',
    json={'agent_id': 'desktop-abc', 'command': 'whoami'}
)
task_id = cmd_response.json()['task_id']
```
