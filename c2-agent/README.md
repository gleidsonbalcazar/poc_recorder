# C2 Agent - Windows Command & Control Agent

Agente Windows que conecta ao servidor C2 via Server-Sent Events (SSE) e executa comandos remotamente.

## 🚀 Tecnologias

- C# / .NET 10
- HttpClient para SSE
- Process API para execução de comandos

## 📋 Características

✅ Conexão via SSE (Server-Sent Events)
✅ Execução de comandos via cmd.exe
✅ Reconexão automática em caso de falha
✅ Envio de resultados ao servidor
✅ Logging detalhado
✅ ID único por agente (hostname + GUID)
✅ Tratamento de timeouts
✅ Shutdown gracioso (Ctrl+C)

## ⚙️ Compilação

### Debug Build:
```bash
cd Agent
dotnet build
```

### Release Build:
```bash
dotnet build -c Release
```

### Executável Standalone (inclui runtime):
```bash
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

O executável estará em: `bin/Release/net10.0/win-x64/publish/Agent.exe`

## ▶️ Execução

### Via dotnet:
```bash
dotnet run
```

### Executável direto:
```bash
Agent.exe
```

### Com URL customizada:
```bash
Agent.exe http://192.168.1.100:8000
```

### Com variável de ambiente:
```cmd
set C2_SERVER_URL=http://192.168.1.100:8000
Agent.exe
```

## 🏗️ Arquitetura

### Program.cs
- Entry point da aplicação
- Loop principal com reconexão automática
- Geração de AgentID único
- Configuração via args/env

### SseClient.cs
- Conexão SSE com o servidor
- Processamento de eventos de comando
- Envio de resultados via HTTP POST
- Logging de atividades

### CommandExecutor.cs
- Execução de comandos via cmd.exe
- Captura de stdout/stderr
- Controle de timeout
- Tratamento de erros

### Models.cs
- DTOs para comunicação:
  - `Command` - comando recebido
  - `Result` - resultado a enviar
  - `AgentConfig` - configuração do agente

## 📡 Comunicação

### Conexão SSE:
```
GET /agent/stream/{agent_id}?hostname={hostname}
```

Mantém conexão aberta e recebe eventos:
```
event: command
data: {"task_id": "uuid-123", "command": "whoami"}
```

### Envio de Resultado:
```
POST /result
Content-Type: application/json

{
  "task_id": "uuid-123",
  "agent_id": "desktop-abc123",
  "output": "output do comando",
  "error": null,
  "exit_code": 0,
  "timestamp": "2025-11-03T10:30:00Z"
}
```

## ⚙️ Configuração

### Parâmetros

| Parâmetro | Descrição | Padrão |
|-----------|-----------|--------|
| ServerUrl | URL do servidor C2 | http://localhost:8000 |
| ReconnectDelayMs | Delay entre reconexões | 5000ms |
| MaxReconnectAttempts | Máximo de tentativas (-1 = infinito) | -1 |
| CommandTimeoutMs | Timeout para comandos | 60000ms |

### Customização

Edite as constantes em `Program.cs`:

```csharp
var config = new AgentConfig
{
    ServerUrl = GetServerUrl(args),
    AgentId = GenerateAgentId(),
    Hostname = Environment.MachineName,
    ReconnectDelayMs = 5000,        // Alterar aqui
    MaxReconnectAttempts = -1        // Alterar aqui
};
```

## 🔒 Segurança

⚠️ **Esta é uma POC para fins educacionais!**

**Limitações conhecidas:**
- ❌ Sem autenticação
- ❌ Sem criptografia (usar HTTP)
- ❌ Sem validação de comandos
- ❌ Executa qualquer comando recebido
- ❌ Sem proteção contra command injection

**Para ambiente de produção:**
- ✅ Implementar autenticação (API Keys, JWT)
- ✅ Usar HTTPS
- ✅ Validar e sanitizar comandos
- ✅ Implementar whitelist de comandos
- ✅ Executar com privilégios mínimos
- ✅ Adicionar rate limiting
- ✅ Logs de auditoria

## 📝 Logging

O agente exibe logs detalhados no console:

```
[19:30:00] Conectando ao servidor SSE: http://localhost:8000/agent/stream/desktop-abc123
[19:30:00] Conectado! Status: OK
[19:30:15] Comando recebido [Task uuid-123]: whoami
[19:30:15] Comando executado [Task uuid-123] - Exit Code: 0
[19:30:15] Resultado enviado para servidor [Task uuid-123]
```

## 🐛 Troubleshooting

### Agente não conecta

1. Verifique se o servidor está rodando
2. Teste a URL manualmente: `curl http://localhost:8000/agents`
3. Verifique firewall
4. Verifique logs do servidor

### Comandos não executam

1. Verifique permissões do usuário
2. Teste o comando manualmente no cmd.exe
3. Verifique timeout (comandos longos podem exceder 60s)
4. Veja os logs para mensagens de erro

### Erro de compilação

```bash
# Limpar build
dotnet clean

# Restaurar pacotes
dotnet restore

# Recompilar
dotnet build
```

## 📦 Distribuição

### Criar executável standalone (recomendado):
```bash
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:PublishTrimmed=true
```

Vantagens:
- Não requer .NET instalado na máquina alvo
- Arquivo único
- Tamanho reduzido (com trimming)

## 🔗 Componentes Relacionados

- **c2-server** - Servidor FastAPI (Python)
- **c2-dashboard** - Dashboard Angular

## 📊 Fluxo de Execução

```
1. Inicialização
   ├─ Gerar AgentID (hostname-guid)
   ├─ Configurar URL do servidor
   └─ Criar SseClient e CommandExecutor

2. Conectar ao Servidor
   ├─ GET /agent/stream/{agent_id}
   ├─ Manter conexão aberta (SSE)
   └─ Aguardar comandos

3. Receber Comando
   ├─ Evento SSE "command"
   ├─ Parse JSON
   └─ Extrair task_id e command

4. Executar Comando
   ├─ Criar Process (cmd.exe)
   ├─ Executar comando
   ├─ Capturar output/error
   └─ Obter exit code

5. Enviar Resultado
   ├─ Criar objeto Result
   ├─ Serializar JSON
   ├─ POST /result
   └─ Aguardar próximo comando

6. Reconexão (se necessário)
   ├─ Detectar desconexão
   ├─ Aguardar delay
   └─ Tentar reconectar
```
