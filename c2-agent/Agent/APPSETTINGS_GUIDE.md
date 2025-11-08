# Guia de Configuração: appsettings.json

Este documento descreve as chaves do `appsettings.json` usadas pelo agente e como elas afetam o comportamento em tempo de execução.

## Mode
- `Mode`: Define o modo de operação do agente.
  - `c2`: Conecta-se ao servidor C2 (SSE) e aguarda comandos. Não inicia workers autônomos.
  - `autonomous`: Executa somente os workers autônomos (gravação/filas/upload), sem C2.
  - `hybrid`: Conecta ao C2 e também inicia os workers autônomos.

## Recording
- Controla o comportamento da gravação de tela/áudio.
- Chaves:
  - `Continuous` (bool):
    - `true`: gravação contínua (sem tempo de término). Com `SegmentSeconds > 0`, os arquivos são criados em segmentos.
    - `false`: gravação agendada com duração fixa em minutos (veja `DurationMinutes`).
  - `IntervalMinutes` (int): intervalo, em minutos, entre gravações no modo agendado. Ex.: Duration=10, Interval=30 → grava 10 min, espera 20 min, repete.
  - `DurationMinutes` (int): duração da gravação no modo agendado.
  - `SegmentSeconds` (int): segundos por segmento. `0` desativa segmentação (gera arquivo único). Valores > 0 habilitam segmentação.
  - `FPS` (int): frames por segundo da captura de tela (ex.: 30).
  - `VideoBitrate` (int): bitrate de vídeo (kbps), ex.: 2000.
  - `CaptureAudio` (bool): `true` para capturar áudio (sistema + microfone) via NAudio.

Notas:
- Em gravação contínua com segmentação (`SegmentSeconds > 0`), o agente monitora a pasta da sessão e enfileira cada segmento como `pending` automaticamente.
- Pastas de saída: `videos/YYYY-MM-DD/session_HHmm` com arquivos `screen_YYYYMMDD_HHMMSS.mp4`.

## Upload
- Controla o envio de vídeos para backend.
- Chaves:
  - `Enabled` (bool): habilita o worker de upload.
  - `PollIntervalSeconds` (int): intervalo de varredura da fila.
  - `MaxConcurrentUploads` (int): limite de uploads simultâneos.
  - `MaxRetries` (int): tentativas por vídeo antes de marcar como `error`.
  - `Endpoint` (string): endpoint HTTP (multipart) alternativo ao TUS (se não usar TUS).
  - `ApiKey` (string): chave opcional para `Endpoint` HTTP.

Regras de prioridade de upload:
- Se `Tus.TusServerUrl` estiver definido → usa TUS.
- Senão, se `Upload.Endpoint` estiver definido → usa HTTP multipart.
- Senão → simula upload (logs no console).

## C2
- Controla a conexão SSE com o servidor de comando & controle.
- Chaves:
  - `Enabled` (bool): habilita conexão SSE.
  - `ServerUrl` (string): ex.: `http://localhost:8000`.
  - `ReconnectDelaySeconds` (int): atraso entre tentativas de reconexão.

## Database
- `Path` (string): caminho do arquivo SQLite relativo ao `Storage.BasePath`. Ex.: `paneas_monitor.db`.

## Storage
- `BasePath` (string): pasta base de armazenamento. Se vazio, usa `%LocalAppData%\C2Agent`.

## Tus
- Configuração do protocolo de upload TUS (`tusd`).
- Chaves:
  - `TusServerUrl` (string): URL do endpoint de criação, ex.: `http://localhost:1080/files/`.
  - `MaxRetries` (int): tentativas em criação/upload.
  - `RetryDelayMs` (int): atraso entre tentativas (ms).

Metadados enviados ao `tusd` (valores codificados em base64 pelo cliente):
- `filename`: nome do arquivo.
- `filetype`: `video/mp4`.
- `processes`: JSON com snapshot de processos da sessão.
- `fileextension`: extensão do arquivo sem o ponto (ex.: `mp4`).
- `stationId`: id único que representa a sessão de gravação (usa `SessionKey`).

## Exemplo completo
```json
{
  "Mode": "hybrid",
  "Recording": {
    "Continuous": true,
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
    "Endpoint": "",
    "ApiKey": ""
  },
  "C2": {
    "Enabled": true,
    "ServerUrl": "http://localhost:8000",
    "ReconnectDelaySeconds": 5
  },
  "Database": { "Path": "paneas_monitor.db" },
  "Storage": { "BasePath": "" },
  "Tus": {
    "TusServerUrl": "http://localhost:1080/files/",
    "MaxRetries": 3,
    "RetryDelayMs": 1000
  }
}
```

