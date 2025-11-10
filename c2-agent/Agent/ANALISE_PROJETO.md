# Análise do Projeto: C2 Agent

## Visão Geral

- Aplicativo console em C# (.NET) que atua como agente C2 com modos autônomo, C2 e híbrido.
- Funcionalidades principais:
  - Captura de tela e áudio (sistema + microfone) com FFmpeg + NAudio.
  - Segmentação automática de vídeo e organização por sessões/pastas por data.
  - Fila local em SQLite para gerenciamento de gravações e tarefas de upload.
  - Upload HTTP (multipart) com acompanhamento de progresso.
  - Cliente SSE para receber comandos do servidor e enviar resultados.
  - Servidor HTTP local para pré-visualização/stream de vídeos com suporte a Range.
- Configuração em `appsettings.json` (modo, gravação, upload, C2, banco e storage).

## Estrutura e Pontos de Entrada

- Entrada: `Program.cs`
  - Carrega configuração com `ConfigManager`.
  - Define base de armazenamento e inicializa `DatabaseManager` (SQLite).
  - Cria `CommandExecutor` (responsável por shell e comandos de mídia).
  - Se C2 habilitado, inicia `SseClient` para receber comandos.
  - Modo autônomo/híbrido: inicializa `VideoRecorderWorker` e `UploadWorker`.
  - Inicia `MediaHttpServer` na porta 9000 (fallback para localhost se necessário).
  - Loop principal gerencia reconexões SSE e finalização graciosa (Ctrl+C).

## Configuração

- Arquivo: `appsettings.json`
  - `Mode`: `c2`, `autonomous`, `hybrid`.
  - `Recording`: contínuo ou agendado; `SegmentSeconds`, `FPS`, `VideoBitrate`, `CaptureAudio` etc.
  - `Upload`: habilita/simula, `Endpoint`, `ApiKey`, concorrência e tentativas.
  - `C2`: `ServerUrl`, `ReconnectDelaySeconds` e toggle de habilitação.
  - `Database`: `Path` do arquivo SQLite.
  - `Storage`: `BasePath` (padrão `%LocalAppData%\C2Agent`).

## C2 e Execução de Comandos

- `SseClient`: conecta a `GET {ServerUrl}/agent/stream/{AgentId}` (SSE) e processa eventos `event: command`.
- `Models.Command` mapeia texto e/ou tipo para `CommandType`:
  - `Shell`: executa `cmd.exe /c ...` com timeout e redirecionamento de saída/erro.
  - Mídia: `VideoStart`, `VideoStop`, `VideoConfig`.
  - Mídia/gerência: `MediaList`, `MediaClean`, `MediaStats`, `MediaDelete`, `MediaListSessions`, `MediaSessionDetails`.
  - `StatusQuery`: retorna status completo do agente.
- Resultados (`Models.Result`) são enviados via `POST {ServerUrl}/result` com JSON (saída, erro, códigos e payloads de mídia/estado).

## Gravação de Vídeo e Áudio

- `FFmpegRecorder`:
  - Captura de tela (`gdigrab`) e áudio via Named Pipe gerado por `AudioManager`.
  - Diretórios: `videos/YYYY-MM-DD/session_HHmm`.
  - Segmentação por `SegmentSeconds` com `-f segment` e `-strftime`; sem segmentação cria arquivo único.
  - Parada graciosa enviando `q` para o stdin do FFmpeg; fallback `Kill()`.
  - Modo periódico: `StartPeriodicRecording()` com intervalo/duração configuráveis.
  - `GetVideoInfo()` retorna tamanho, datas e estimativa simples de duração por bitrate.
- `AudioManager` (NAudio):
  - Captura áudio do sistema (`WasapiLoopbackCapture`) e microfone (`WasapiCapture`).
  - Resample para 48 kHz estéreo, mixa e escreve PCM 16-bit no Named Pipe `\\.\pipe\C2Agent_Audio` com pacing.
- `FFmpegHelper`:
  - Localiza `ffmpeg.exe` (diretório local `./ffmpeg` ou PATH) e baixa automaticamente um build “essentials” se ausente.
  - Constrói argumentos de linha de comando (com e sem pipe) e lista dispositivos de áudio (`-f dshow`).

## Armazenamento e Servidor HTTP

- `MediaStorage`:
  - Lista, apaga e sumariza arquivos de mídia (foco em `.mp4`).
  - Agrupamento de segmentos por sessão (prefixos `screen_YYYYMMDD_HHMM`).
  - Metadados opcionais em JSON (salvar/carregar) e preparo de upload (content-type, checksum SHA-256).
- `MediaHttpServer` (porta 9000):
  - `HttpListener` em `http://+:9000/` (fallback `http://localhost:9000/` se sem privilégios).
  - Endpoint simples `GET /media/{filename}` com CORS e suporte a Range.
  - Bloqueia servir arquivo que esteja sendo gravado (verifica sessão/segmentos recentes).

## Fila (SQLite) e Upload

- `DatabaseManager`:
  - Tabelas: `video_queue` e `upload_tasks`, com índices.
  - Operações: inserir/atualizar vídeos, obter pendentes, progresso de upload e estatísticas de fila.
- `VideoRecorderWorker`:
  - Modo contínuo: inicia gravação indefinida e registra sessão.
  - Modo agendado: grava por duração fixa, atualiza tamanho total e marca como `pending`.
- `UploadWorker`:
  - Varre pendentes, valida arquivo/diretório, cria `upload_task` e realiza upload real (`HttpUploadClient`) ou simulado se `UploadEndpoint` não definido.
  - Retentativas limitadas; atualiza status (`pending`, `uploading`, `done`, `error`) e progresso.
- `HttpUploadClient`:
  - Multipart com `ProgressStreamContent` para reportar bytes enviados.
  - Suporta uploads de arquivo único e pastas segmentadas (vários `.mp4`).

## Status do Agente

- `status:query` retorna `AgentStatusResult`:
  - `RecordingStatusResult`: estado de gravação (sessão, início, duração, contagem de segmentos, modo).
  - `DatabaseStatsResult`: contagem por status e tamanho total (nota: implementado parcialmente no `CommandExecutor`).
  - `UploadStatusResult`: habilitado, uploads ativos, endpoint.
  - `SystemInfoResult`: versão do OS, caminho de storage e espaço livre estimado.

## Observações e Requisitos

- Target Framework: `net10.0` (requer SDK .NET recente). Ajuste para `net8.0`/`net9.0` se necessário.
- `FFmpegHelper.EnsureFFmpegAvailable()` baixa o ZIP de builds do FFmpeg se `ffmpeg.exe` não existir (requer rede).
- `MediaHttpServer` em interfaces externas requer privilégios/admin ou configuração `netsh http add urlacl`.
- Alguns logs possuem acentuação corrompida na saída do console; funcionalidade não é afetada.

## Como Executar (resumo)

1. Instale .NET SDK compatível com `TargetFramework` configurado no projeto.
2. Execute a aplicação (ex.: `dotnet run -c Release`).
3. Ajuste `appsettings.json` conforme desejado:
   - `Mode`: `hybrid` para C2 + autônomo; `autonomous` sem C2; `c2` somente comandos.
   - Configure `Upload.Endpoint` e `ApiKey` para upload real.
4. Acesse o servidor de pré-visualização em `http://localhost:9000/` e sirva arquivos via `GET /media/{nome.mp4}`.

---

Referências principais:
- `Program.cs`
- `ConfigManager.cs`, `appsettings.json`
- `SseClient.cs`, `CommandExecutor.cs`, `Models.cs`
- `FFmpegRecorder.cs`, `FFmpegHelper.cs`, `AudioManager.cs`
- `MediaStorage.cs`, `MediaHttpServer.cs`
- `Database/DatabaseManager.cs` e `Database/Models/*`
- `Workers/VideoRecorderWorker.cs`, `Workers/UploadWorker.cs`, `Workers/ProcessMonitor.cs`

