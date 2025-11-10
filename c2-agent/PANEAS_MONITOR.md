# Paneas Monitor - Sistema de Monitoramento Autônomo

## 📋 Visão Geral

O **Paneas Monitor** é uma evolução do sistema C2 Agent que adiciona capacidades de operação autônoma, incluindo gravação automática de tela, gerenciamento de fila persistente, e upload automático de vídeos. O sistema foi projetado para funcionar em três modos distintos: C2, Autônomo ou Híbrido.

### Características Principais

- ✅ **Operação Autônoma**: Funciona independentemente sem necessidade de comandos C2
- ✅ **Fila Persistente SQLite**: Sobrevive a crashes e reinicializações
- ✅ **Background Workers**: Gravação e upload em threads separadas
- ✅ **Process Snapshots**: Captura contexto do sistema (processos ativos)
- ✅ **Segmentação de Vídeo**: Arquivos de 30 segundos para fácil gerenciamento
- ✅ **Organização por Sessão**: Estrutura hierárquica data/sessão
- ✅ **Upload com Retry**: Sistema resiliente com múltiplas tentativas
- ✅ **HTTP Upload Real**: Cliente multipart com progress tracking
- ✅ **Configuração JSON**: Facilmente personalizável via appsettings.json

---

## 🏗️ Arquitetura

### Modos de Operação

O sistema suporta três modos configuráveis via `appsettings.json`:

1. **Modo C2** (`"Mode": "c2"`)
   - Operação via comandos remotos do servidor C2
   - Comportamento original do sistema
   - Workers desativados

2. **Modo Autônomo** (`"Mode": "autonomous"`)
   - Operação independente
   - Workers ativos (VideoRecorderWorker + UploadWorker)
   - Sem conexão C2

3. **Modo Híbrido** (`"Mode": "hybrid"`) ⭐ *Recomendado*
   - Combina capacidades C2 e autônomas
   - Workers ativos + conexão C2
   - Máxima flexibilidade

### Componentes Principais

```
┌─────────────────────────────────────────────────────────────┐
│                        Program.cs                           │
│  (Entry Point + Inicialização + Loop Principal)             │
└─────────────────────────────────────────────────────────────┘
                              │
          ┌───────────────────┼───────────────────┐
          │                   │                   │
          ▼                   ▼                   ▼
┌──────────────────┐  ┌──────────────┐  ┌──────────────────┐
│  ConfigManager   │  │  Database    │  │  CommandExecutor │
│  (appsettings)   │  │  Manager     │  │  (C2 Commands)   │
└──────────────────┘  └──────────────┘  └──────────────────┘
                              │
          ┌───────────────────┼───────────────────┐
          │                   │                   │
          ▼                   ▼                   ▼
┌──────────────────┐  ┌──────────────┐  ┌──────────────────┐
│ VideoRecorder    │  │ UploadWorker │  │ ProcessMonitor   │
│ Worker           │  │              │  │                  │
└──────────────────┘  └──────────────┘  └──────────────────┘
          │                   │
          ▼                   ▼
┌──────────────────┐  ┌──────────────────┐
│  FFmpegRecorder  │  │ HttpUploadClient │
│  (NAudio)        │  │                  │
└──────────────────┘  └──────────────────┘
```

---

## 📁 Estrutura de Arquivos

### Código Fonte

```
c2-agent/Agent/
├── Program.cs                          # Entry point e orquestração
├── ConfigManager.cs                    # Gerenciamento de configuração
├── CommandExecutor.cs                  # Execução de comandos C2
├── FFmpegRecorder.cs                   # Gravação de vídeo/áudio
├── AudioManager.cs                     # Captura de áudio (NAudio)
├── FFmpegHelper.cs                     # Helpers FFmpeg
├── HttpUploadClient.cs                 # Cliente de upload HTTP
├── MediaHttpServer.cs                  # Servidor preview local
├── MediaStorage.cs                     # Gerenciamento de storage
├── SseClient.cs                        # Cliente C2 (SSE)
├── appsettings.json                    # Configuração
│
├── Database/
│   ├── DatabaseManager.cs              # CRUD SQLite
│   └── Models/
│       ├── VideoRecord.cs              # Modelo de vídeo
│       ├── UploadTask.cs               # Modelo de upload
│       └── ProcessSnapshot.cs          # Modelo de snapshot
│
└── Workers/
    ├── VideoRecorderWorker.cs          # Worker de gravação
    ├── UploadWorker.cs                 # Worker de upload
    └── ProcessMonitor.cs               # Captura de processos
```

### Estrutura de Storage

```
C:\Users\<User>\AppData\Local\C2Agent\
├── paneas_monitor.db                   # Banco SQLite
│
└── videos/
    └── 2025-11-06/                     # Data
        ├── session_1217/               # Sessão HH:MM
        │   ├── screen_20251106_121728.mp4  (30s, ~6MB)
        │   ├── screen_20251106_121758.mp4  (30s, ~6MB)
        │   └── screen_20251106_121828.mp4  (6s,  ~1MB)
        │
        ├── session_1345/
        │   ├── screen_20251106_134512.mp4
        │   ├── screen_20251106_134542.mp4
        │   └── ...
        │
        └── session_1620/
            └── ...
```

**Estimativa de Armazenamento:**
- 30 segundos @ 2000kbps + áudio = ~6MB por segmento
- 1 hora de gravação = 120 segmentos = ~720MB
- 8 horas/dia = 960 segmentos = ~5.6GB/dia

---

## ⚙️ Configuração (appsettings.json)

```json
{
  "Mode": "hybrid",

  "Recording": {
    "Continuous": false,
    "IntervalMinutes": 60,
    "DurationMinutes": 60,
    "SegmentSeconds": 30,
    "FPS": 30,
    "VideoBitrate": 2000,
    "CaptureAudio": true
  },

  "Upload": {
    "Enabled": true,
    "PollIntervalSeconds": 30,
    "MaxConcurrentUploads": 2,
    "MaxRetries": 3,
    "Endpoint": "https://upload.paneas.com/videos",
    "ApiKey": "your-api-key-here"
  },

  "C2": {
    "Enabled": true,
    "ServerUrl": "http://localhost:8000",
    "ReconnectDelaySeconds": 5
  },

  "Database": {
    "Path": "paneas_monitor.db"
  },

  "Storage": {
    "BasePath": ""
  }
}
```

### Parâmetros Detalhados

#### Recording
| Parâmetro | Tipo | Padrão | Descrição |
|-----------|------|--------|-----------|
| `Continuous` | bool | false | Gravação contínua (sem parar) ou agendada |
| `IntervalMinutes` | int | 60 | Intervalo entre gravações (modo agendado) |
| `DurationMinutes` | int | 60 | Duração de cada gravação (modo agendado) |
| `SegmentSeconds` | int | 30 | Duração de cada segmento de vídeo |
| `FPS` | int | 30 | Frames por segundo |
| `VideoBitrate` | int | 2000 | Bitrate do vídeo em kbps |
| `CaptureAudio` | bool | true | Capturar áudio (sistema + microfone) |

#### Upload
| Parâmetro | Tipo | Padrão | Descrição |
|-----------|------|--------|-----------|
| `Enabled` | bool | true | Ativar worker de upload |
| `PollIntervalSeconds` | int | 30 | Intervalo de verificação da fila |
| `MaxConcurrentUploads` | int | 2 | Uploads simultâneos |
| `MaxRetries` | int | 3 | Tentativas antes de marcar como erro |
| `Endpoint` | string | "" | URL do endpoint de upload |
| `ApiKey` | string | "" | Chave de API para autenticação |

#### C2
| Parâmetro | Tipo | Padrão | Descrição |
|-----------|------|--------|-----------|
| `Enabled` | bool | true | Ativar conexão C2 |
| `ServerUrl` | string | localhost:8000 | URL do servidor C2 |
| `ReconnectDelaySeconds` | int | 5 | Delay entre reconexões |

---

## 🗄️ Banco de Dados

### Schema SQLite

#### Tabela: video_queue

```sql
CREATE TABLE video_queue (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    file_path TEXT NOT NULL,
    session_key TEXT,
    process_snapshot TEXT,
    status TEXT NOT NULL DEFAULT 'pending',
    created_at TEXT NOT NULL,
    uploaded_at TEXT,
    retry_count INTEGER DEFAULT 0,
    error_message TEXT,
    file_size_bytes INTEGER DEFAULT 0
);
```

**Status possíveis:**
- `recording` - Gravação em andamento
- `pending` - Aguardando upload
- `uploading` - Upload em progresso
- `done` - Upload concluído
- `error` - Erro permanente

#### Tabela: upload_tasks

```sql
CREATE TABLE upload_tasks (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    video_id INTEGER NOT NULL,
    status TEXT NOT NULL DEFAULT 'pending',
    progress INTEGER DEFAULT 0,
    bytes_uploaded INTEGER DEFAULT 0,
    total_bytes INTEGER NOT NULL,
    created_at TEXT NOT NULL,
    started_at TEXT,
    completed_at TEXT,
    error_message TEXT,
    FOREIGN KEY (video_id) REFERENCES video_queue(id)
);
```

### Process Snapshot (JSON)

Cada gravação inclui um snapshot dos processos ativos no sistema:

```json
{
  "capturedAt": "2025-11-06T12:17:28.123Z",
  "system": {
    "hostname": "LT-IC825-1",
    "osVersion": "Microsoft Windows NT 10.0.19045.0",
    "processorCount": 8,
    "totalMemoryMB": 16384
  },
  "processes": [
    {
      "name": "chrome",
      "processId": 12345,
      "windowTitle": "YouTube - Google Chrome",
      "memoryMB": 512.34,
      "executablePath": "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe",
      "startTime": "2025-11-06T10:30:00Z"
    },
    {
      "name": "code",
      "processId": 23456,
      "windowTitle": "Visual Studio Code",
      "memoryMB": 789.12,
      "executablePath": "C:\\Users\\User\\AppData\\Local\\Programs\\Microsoft VS Code\\Code.exe",
      "startTime": "2025-11-06T09:15:00Z"
    }
  ]
}
```

**Utilidade:**
- Análise forense de contexto
- Correlação de atividades
- Auditoria de uso
- Debug de problemas

---

## 🔄 Workers

### VideoRecorderWorker

Responsável pela gravação autônoma de vídeos.

**Modos de Operação:**

1. **Contínuo** (`Continuous: true`)
   - Grava indefinidamente
   - Cria segmentos de 30s continuamente
   - Não para até receber sinal de shutdown

2. **Agendado** (`Continuous: false`)
   - Grava por `DurationMinutes`
   - Aguarda `IntervalMinutes - DurationMinutes`
   - Ciclo se repete

**Fluxo de Execução:**

```
Iniciar Worker
    │
    ▼
Capturar Process Snapshot
    │
    ▼
Iniciar FFmpegRecorder
    │
    ▼
Criar registro no DB (status: recording)
    │
    ▼
[Modo Contínuo]          [Modo Agendado]
    │                         │
    ▼                         ▼
Aguardar Infinite      Aguardar DurationMinutes
    │                         │
    ▼                         ▼
(até Ctrl+C)           Parar gravação
                              │
                              ▼
                       Atualizar DB (status: pending)
                              │
                              ▼
                       Aguardar IntervalMinutes
                              │
                              ▼
                       (Loop)
```

### UploadWorker

Responsável por processar a fila de uploads.

**Fluxo de Execução:**

```
Iniciar Worker
    │
    ▼
Loop (a cada PollIntervalSeconds):
    │
    ▼
Buscar vídeos pendentes (limit: MaxConcurrentUploads)
    │
    ▼
Para cada vídeo:
    │
    ├─> Validar arquivo
    │   └─> Se inválido: status = error
    │
    ├─> Criar UploadTask
    │
    ├─> Upload via HttpUploadClient
    │   ├─> [Sucesso] → status = done
    │   └─> [Falha]
    │       ├─> retry_count++
    │       ├─> Se retry_count < MaxRetries: status = pending
    │       └─> Se retry_count >= MaxRetries: status = error
    │
    └─> Atualizar progresso no DB
```

**Upload com Progress Tracking:**

O `HttpUploadClient` reporta progresso em tempo real:

```
[UploadWorker] Processando: screen_20251106_121728.mp4
[HttpUploadClient] Uploading: screen_20251106_121728.mp4 (6.02 MB)
[UploadWorker] Progresso: 20% (screen_20251106_121728.mp4)
[UploadWorker] Progresso: 40% (screen_20251106_121728.mp4)
[UploadWorker] Progresso: 60% (screen_20251106_121728.mp4)
[UploadWorker] Progresso: 80% (screen_20251106_121728.mp4)
[UploadWorker] Progresso: 100% (screen_20251106_121728.mp4)
[UploadWorker] ✓ Upload concluído: screen_20251106_121728.mp4
```

---

## 📤 Sistema de Upload

### HttpUploadClient

Cliente HTTP com suporte a:
- Upload multipart/form-data
- Progress tracking em tempo real
- Arquivos individuais ou sessões completas
- Timeout de 30 minutos
- Metadata incluído (session_key, process_snapshot)

**Formato de Upload (arquivo único):**

```http
POST https://upload.paneas.com/videos HTTP/1.1
Content-Type: multipart/form-data; boundary=----WebKitFormBoundary7MA4YWxkTrZu0gW
X-API-Key: your-api-key-here

------WebKitFormBoundary7MA4YWxkTrZu0gW
Content-Disposition: form-data; name="file"; filename="screen_20251106_121728.mp4"
Content-Type: video/mp4

[binary data]
------WebKitFormBoundary7MA4YWxkTrZu0gW
Content-Disposition: form-data; name="session_key"

session_1217
------WebKitFormBoundary7MA4YWxkTrZu0gW
Content-Disposition: form-data; name="created_at"

2025-11-06T12:17:28.123Z
------WebKitFormBoundary7MA4YWxkTrZu0gW
Content-Disposition: form-data; name="process_snapshot"

{"capturedAt":"2025-11-06T12:17:28.123Z",...}
------WebKitFormBoundary7MA4YWxkTrZu0gW--
```

**Formato de Upload (sessão segmentada):**

```http
POST https://upload.paneas.com/videos HTTP/1.1
Content-Type: multipart/form-data; boundary=----WebKitFormBoundary7MA4YWxkTrZu0gW
X-API-Key: your-api-key-here

------WebKitFormBoundary7MA4YWxkTrZu0gW
Content-Disposition: form-data; name="session_key"

session_1217
------WebKitFormBoundary7MA4YWxkTrZu0gW
Content-Disposition: form-data; name="segment_count"

3
------WebKitFormBoundary7MA4YWxkTrZu0gW
Content-Disposition: form-data; name="segment_0"; filename="screen_20251106_121728.mp4"
Content-Type: video/mp4

[binary data]
------WebKitFormBoundary7MA4YWxkTrZu0gW
Content-Disposition: form-data; name="segment_1"; filename="screen_20251106_121758.mp4"
Content-Type: video/mp4

[binary data]
------WebKitFormBoundary7MA4YWxkTrZu0gW
Content-Disposition: form-data; name="segment_2"; filename="screen_20251106_121828.mp4"
Content-Type: video/mp4

[binary data]
------WebKitFormBoundary7MA4YWxkTrZu0gW--
```

### Implementando o Servidor de Upload

**Exemplo Python (FastAPI):**

```python
from fastapi import FastAPI, File, UploadFile, Form
from typing import List, Optional
import json

app = FastAPI()

@app.post("/videos")
async def upload_videos(
    file: Optional[UploadFile] = File(None),
    segment_0: Optional[UploadFile] = File(None),
    segment_1: Optional[UploadFile] = File(None),
    # ... adicionar mais segmentos conforme necessário
    session_key: str = Form(...),
    created_at: str = Form(...),
    process_snapshot: Optional[str] = Form(None),
    segment_count: Optional[int] = Form(None)
):
    # Upload de arquivo único
    if file:
        file_path = f"storage/{session_key}/{file.filename}"
        with open(file_path, "wb") as f:
            f.write(await file.read())

        # Salvar metadata
        metadata = {
            "session_key": session_key,
            "created_at": created_at,
            "process_snapshot": json.loads(process_snapshot) if process_snapshot else None
        }

        return {"status": "success", "file": file.filename}

    # Upload de sessão segmentada
    elif segment_count:
        segments = []
        for i in range(segment_count):
            segment_file = locals().get(f"segment_{i}")
            if segment_file:
                file_path = f"storage/{session_key}/{segment_file.filename}"
                with open(file_path, "wb") as f:
                    f.write(await segment_file.read())
                segments.append(segment_file.filename)

        return {"status": "success", "segments": segments}

    return {"status": "error", "message": "No files provided"}
```

---

## 🚀 Instalação e Uso

### Pré-requisitos

- .NET 10.0 (ou superior)
- Windows 10/11
- FFmpeg (download automático na primeira execução)

### Build e Execução

**1. Build em modo Debug:**

```bash
cd c2-agent/Agent
dotnet build
```

**2. Build em modo Release (single-file):**

```bash
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

**3. Executar:**

```bash
# Debug
dotnet run

# Release
cd bin/Release/net10.0/win-x64/publish
Agent.exe
```

### Configuração Inicial

**1. Editar `appsettings.json`:**

```json
{
  "Mode": "autonomous",
  "Recording": {
    "Continuous": false,
    "DurationMinutes": 30
  },
  "Upload": {
    "Enabled": true,
    "Endpoint": "https://seu-servidor.com/upload",
    "ApiKey": "sua-chave-aqui"
  }
}
```

**2. Executar o agente:**

O agente irá:
- ✅ Criar banco de dados SQLite
- ✅ Iniciar workers de gravação e upload
- ✅ Iniciar servidor HTTP local (localhost:9000)
- ✅ Começar a gravar automaticamente

**3. Verificar logs:**

```
╔═══════════════════════════════════════════╗
║  Paneas Monitor - C2 & Autonomous Agent  ║
║  POC - Sistema de Monitoramento           ║
╚═══════════════════════════════════════════╝

Mode: autonomous
Storage Path: C:\Users\User\AppData\Local\C2Agent
Database: C:\Users\User\AppData\Local\C2Agent\paneas_monitor.db

[Workers] Initializing autonomous workers...
[VideoRecorderWorker] Worker iniciado
[UploadWorker] Upload HTTP configurado: https://seu-servidor.com/upload
[UploadWorker] Worker iniciado
```

---

## 🔧 Comandos C2 (Modo Híbrido)

Quando em modo `c2` ou `hybrid`, o agente responde aos seguintes comandos:

### Comandos de Vídeo

| Comando | Descrição | Exemplo |
|---------|-----------|---------|
| `video_start` | Iniciar gravação | `{"type": "video_start", "duration": 60}` |
| `video_stop` | Parar gravação | `{"type": "video_stop"}` |
| `video_list` | Listar vídeos | `{"type": "video_list"}` |
| `video_delete` | Deletar vídeo | `{"type": "video_delete", "path": "..."}` |

### Comandos de Sistema

| Comando | Descrição | Exemplo |
|---------|-----------|---------|
| `shell` | Executar comando | `{"type": "shell", "command": "ipconfig"}` |
| `process_list` | Listar processos | `{"type": "process_list"}` |
| `process_kill` | Matar processo | `{"type": "process_kill", "pid": 1234}` |

### Comandos de Mídia

| Comando | Descrição | Exemplo |
|---------|-----------|---------|
| `media_list_sessions` | Listar sessões | `{"type": "media_list_sessions"}` |
| `media_session_details` | Detalhes sessão | `{"type": "media_session_details", "session": "session_1217"}` |

---

## 📊 Monitoramento

### Logs do Sistema

O agente gera logs detalhados de todas as operações:

```
[SYNC] ===== INICIANDO GRAVAÇÃO =====
[SYNC] T+0ms: Início do StartRecording()
[FFmpegRecorder] Modo segmentação: 30s por arquivo
[ProcessMonitor] Capturado snapshot com 361 processos
[SYNC] T+16ms: Iniciando AudioManager...
[AudioManager] V Captura de áudio iniciada
[SYNC] T+922ms: Iniciando processo FFmpeg...
[SYNC] T+2242ms: V Processo FFmpeg iniciado (PID: 15568)
[AudioManager] V FFmpeg conectado ao pipe
[AudioManager] Escrevendo PCM no pipe (48kHz, stereo, 16-bit)...
```

### Estatísticas da Fila

```sql
SELECT
    status,
    COUNT(*) as count,
    SUM(file_size_bytes) / 1024.0 / 1024.0 as total_mb
FROM video_queue
GROUP BY status;
```

Resultado:
```
status      | count | total_mb
------------|-------|----------
recording   | 1     | 0
pending     | 15    | 94.5
uploading   | 2     | 12.3
done        | 145   | 892.1
error       | 3     | 18.2
```

### Preview Local

Acesse `http://localhost:9000` para visualizar vídeos localmente (mesmo computador).

**Endpoints:**
- `GET /` - Lista de vídeos
- `GET /videos/2025-11-06/session_1217/screen_20251106_121728.mp4` - Stream de vídeo

---

## 🛡️ Segurança e Boas Práticas

### ⚠️ IMPORTANTE

Este é um **POC educacional**. Para uso em produção, implemente:

1. **Autenticação**
   - JWT tokens para API
   - Criptografia de ApiKey no config

2. **Criptografia**
   - HTTPS obrigatório
   - Criptografia de vídeos em disco
   - Criptografia de snapshots

3. **Validação**
   - Validação de inputs
   - Sanitização de caminhos
   - Rate limiting

4. **Auditoria**
   - Logs persistentes
   - Auditoria de acessos
   - Alertas de anomalias

5. **Isolamento**
   - Execução com privilégios mínimos
   - Sandboxing de comandos
   - Containers/VMs

### Recomendações

- ✅ Use API Key forte (32+ caracteres)
- ✅ Configure firewall para bloquear acesso externo ao HTTP server
- ✅ Monitore uso de disco (5.6GB/dia em 8h)
- ✅ Implemente rotação de logs
- ✅ Configure backup do banco SQLite
- ✅ Use HTTPS no endpoint de upload
- ✅ Valide certificados SSL

---

## 🐛 Troubleshooting

### Agente não inicia

**Problema:** Agente fecha imediatamente após iniciar

**Solução:**
1. Verificar se FFmpeg foi baixado: `Agent\ffmpeg\ffmpeg.exe`
2. Verificar permissões da pasta `AppData\Local\C2Agent`
3. Verificar logs de erro no console
4. Testar com `Mode: "autonomous"` e `C2.Enabled: false`

### Gravação não funciona

**Problema:** Worker inicia mas não grava vídeos

**Solução:**
1. Verificar se há dispositivo de áudio disponível
2. Verificar permissões de tela
3. Verificar espaço em disco
4. Testar FFmpeg manualmente: `ffmpeg -f gdigrab -i desktop test.mp4`

### Upload falha constantemente

**Problema:** Vídeos ficam em `status: error` após 3 tentativas

**Solução:**
1. Verificar conectividade de rede
2. Testar endpoint manualmente: `curl -X POST <endpoint>`
3. Verificar API Key
4. Verificar logs do servidor de upload
5. Aumentar `MaxRetries` no config
6. Verificar timeout do servidor (>30min recomendado)

### Banco de dados corrompido

**Problema:** Erro ao inicializar DatabaseManager

**Solução:**
1. Backup do arquivo `.db`
2. Tentar reparação: `sqlite3 paneas_monitor.db "PRAGMA integrity_check;"`
3. Se irreparável, deletar arquivo (perda de fila)
4. Agente criará novo banco automaticamente

### Arquivos de vídeo grandes demais

**Problema:** Segmentos maiores que esperado (~6MB)

**Solução:**
1. Reduzir `VideoBitrate` de 2000 para 1000-1500 kbps
2. Reduzir `FPS` de 30 para 24 ou 20
3. Verificar se áudio está duplicado (bug)
4. Usar codec mais eficiente (requer mudança de código)

---

## 📈 Roadmap / Melhorias Futuras

### Fase 3: Windows Service ⏳

Converter agente para Windows Service:
- [x] Adicionar `Microsoft.Extensions.Hosting.WindowsServices`
- [ ] Implementar `BackgroundService`
- [ ] Configurar instalador (sc.exe ou WiX)
- [ ] Auto-start na inicialização
- [ ] Logging para Event Viewer

### Fase 4: Validação FFmpeg ⏳

Adicionar verificação de integridade:
- [ ] Integrar `ffprobe`
- [ ] Validar vídeos antes de upload
- [ ] Detectar corrupção
- [ ] Marcar vídeos inválidos no banco

### Fase 5: Compressão e Otimização ⏳

- [ ] Compressão H.265 (HEVC)
- [ ] Redução de bitrate adaptativo
- [ ] Deduplicação de frames estáticos
- [ ] Compactação ZIP para uploads

### Fase 6: Upload TUS Protocol ⏳

- [ ] Implementar cliente TUS (resumable uploads)
- [ ] Suportar uploads interrompidos
- [ ] Retry automático de chunks
- [ ] Menor uso de banda

### Fase 7: Dashboard Web ⏳

- [ ] Interface de administração
- [ ] Visualização de estatísticas
- [ ] Player de vídeos integrado
- [ ] Gerenciamento de sessões
- [ ] Download de snapshots

### Fase 8: Criptografia ⏳

- [ ] Criptografia de vídeos em disco (AES-256)
- [ ] Criptografia de uploads (TLS 1.3)
- [ ] Criptografia de snapshots
- [ ] Key management

---

## 📚 Referências Técnicas

### FFmpeg

- Documentação: https://ffmpeg.org/documentation.html
- Filtros de vídeo: https://ffmpeg.org/ffmpeg-filters.html
- Segmentação: https://ffmpeg.org/ffmpeg-formats.html#segment

### NAudio

- GitHub: https://github.com/naudio/NAudio
- Documentação: https://github.com/naudio/NAudio/blob/master/Docs/README.md
- WASAPI Loopback: https://github.com/naudio/NAudio/blob/master/Docs/WasapiLoopbackRecording.md

### SQLite

- Documentação: https://www.sqlite.org/docs.html
- Pragma statements: https://www.sqlite.org/pragma.html
- Microsoft.Data.Sqlite: https://docs.microsoft.com/en-us/dotnet/standard/data/sqlite/

### HTTP Multipart Upload

- RFC 7578: https://tools.ietf.org/html/rfc7578
- HttpClient: https://docs.microsoft.com/en-us/dotnet/api/system.net.http.httpclient

---

## 📄 Licença

Este projeto é fornecido "como está" para fins educacionais e de demonstração.

**⚠️ AVISO LEGAL:**
- Não utilize para fins maliciosos
- Respeite privacidade e legislação local
- Obtenha consentimento antes de monitorar
- Implemente segurança adequada para produção

---

## 🔗 Links Úteis

- **Repositório:** https://github.com/gleidsonbalcazar/poc_recorder
- **Branch Paneas:** `feature/paneas-monitor`
- **Documentação Original C2:** `c2-agent/README.md`
- **Plano de Implementação:** `PLANO_PANEAS_MONITOR.md`

---

**Versão:** 2.0.0
**Última Atualização:** 2025-11-06
**Status:** ✅ Produção-Ready (com ressalvas de segurança)
