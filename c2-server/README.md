# C2 Server - Command and Control Server

Servidor FastAPI que gerencia conexões de agentes via Server-Sent Events (SSE) e fornece API REST para o dashboard.

## 🚀 Instalação

### Pré-requisitos
- Python 3.8 ou superior
- pip

### Setup

1. Criar ambiente virtual:
```bash
python -m venv venv
```

2. Ativar ambiente virtual:
```bash
# Windows
venv\Scripts\activate

# Linux/Mac
source venv/bin/activate
```

3. Instalar dependências:
```bash
pip install -r requirements.txt
```

## ▶️ Execução

### Modo desenvolvimento (com reload):
```bash
uvicorn server:app --reload --host localhost --port 8000
```

### Modo produção:
```bash
python server.py
```

O servidor estará disponível em: `http://localhost:8000`

## 📡 API Endpoints

### Status do Servidor
- **GET** `/` - Status e informações do servidor

### Gerenciamento de Agentes
- **GET** `/agents` - Lista todos os agentes conectados
- **GET** `/agent/stream/{agent_id}?hostname=NAME` - SSE stream para agentes (conexão persistente)
- **DELETE** `/agent/{agent_id}` - Remove agente da lista

### Comandos
- **POST** `/command` - Envia comando para um agente específico
  ```json
  {
    "agent_id": "DESKTOP-WIN11-abc123",
    "command": "whoami"
  }
  ```

### Resultados
- **POST** `/result` - Agente envia resultado de execução
  ```json
  {
    "task_id": "uuid-123",
    "agent_id": "DESKTOP-WIN11-abc123",
    "output": "desktop-win11\\user",
    "error": null,
    "exit_code": 0,
    "timestamp": "2025-11-03T10:35:00Z"
  }
  ```

- **GET** `/result/{task_id}` - Consulta resultado específico
- **GET** `/results?limit=50` - Lista resultados recentes

## 🏗️ Arquitetura

### Estruturas de Dados

**Agentes conectados:**
```python
agents = {
    "agent_id": {
        "agent_id": "DESKTOP-WIN11-abc123",
        "hostname": "DESKTOP-WIN11",
        "connected_at": "2025-11-03T10:30:00Z",
        "last_seen": "2025-11-03T10:35:00Z",
        "status": "online"
    }
}
```

**Filas de comandos:**
```python
command_queues = {
    "agent_id": Queue()  # Fila de comandos pendentes
}
```

**Resultados:**
```python
results = {
    "task_id": {
        "task_id": "uuid-123",
        "agent_id": "agent_id",
        "command": "whoami",
        "output": "output text",
        "error": null,
        "exit_code": 0,
        "timestamp": "2025-11-03T10:35:00Z",
        "status": "completed"
    }
}
```

## 🔄 Fluxo SSE

1. Agente conecta em `/agent/stream/{agent_id}`
2. Servidor mantém conexão aberta
3. Envia heartbeat a cada 1 segundo (`: heartbeat`)
4. Quando há comando na fila, envia:
   ```
   event: command
   data: {"task_id": "uuid", "command": "whoami"}

   ```

## 🔧 CORS

CORS configurado para aceitar requisições de:
- `http://localhost:4200` (Angular dev server)
- `http://127.0.0.1:4200`

## 📊 Monitoramento

- Agentes são considerados **offline** se `last_seen` > 60 segundos
- Heartbeat mantém `last_seen` atualizado
- Status disponível em `GET /agents`

## 🧪 Testes

### Testar servidor:
```bash
curl http://localhost:8000/
```

### Listar agentes:
```bash
curl http://localhost:8000/agents
```

### Enviar comando:
```bash
curl -X POST http://localhost:8000/command \
  -H "Content-Type: application/json" \
  -d '{"agent_id": "test-agent", "command": "whoami"}'
```

## 📝 Notas

- Este é um servidor **POC** sem autenticação ou criptografia
- Armazena dados **em memória** (não persistente)
- Reiniciar o servidor limpa todos os dados
- Para produção: adicionar autenticação, HTTPS, banco de dados

## 🔗 Componentes Relacionados

- **c2-dashboard** - Dashboard Angular para interface web
- **c2-agent** - Agente C# para Windows
