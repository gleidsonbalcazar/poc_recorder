# Arquitetura: Configuração Centralizada via API

**Data:** 2025-11-14
**Versão:** 1.0
**Status:** Proposta para implementação

---

## 📋 Resumo Executivo

Sistema de configuração centralizada onde o Agent busca sua configuração de uma API backend ao iniciar, com fallback em cascata para garantir disponibilidade mesmo offline.

### Ordem de Prioridade:
1. **API Backend** (prioridade máxima)
2. **Cache local** (última configuração conhecida)
3. **appsettings.json** (fallback final - valores padrão)

---

## 🎯 Objetivos

### Funcionalidades:
- ✅ Configuração específica por **Agent ID / Máquina / Usuário**
- ✅ Gestão centralizada de N agents de um único lugar
- ✅ Suporte a agendamento personalizado por backend
- ✅ Fallback robusto para operação offline
- ✅ Cache local de última configuração conhecida
- ✅ Hot-reload via API (sem reinstalar Agent)

### Benefícios:
- 📊 **Escalabilidade:** Gerenciar 1000+ agents centralmente
- 🔄 **Flexibilidade:** Alterar configs sem reinstalar MSI
- 🎛️ **Granularidade:** Configs diferentes por perfil/grupo
- 📈 **Auditoria:** Rastreabilidade de configurações
- 🧪 **A/B Testing:** Experimentos controlados
- ⚡ **Rollback rápido:** Reverter mudanças instantaneamente

---

## 🏗️ Arquitetura

### Fluxo de Inicialização

```
┌─────────────┐
│ Agent Start │
└──────┬──────┘
       │
       ▼
┌────────────────────────────────────┐
│ 1. Busca Config da API (PRIORIDADE)│
│    POST /api/agent-config           │
│    {                                │
│      agentId, hostname, username    │
│    }                                │
└──────┬─────────────────────────────┘
       │
       ├─── ✅ API Sucesso ──────────┐
       │                              │
       │                              ▼
       │                  ┌───────────────────────┐
       │                  │ 2. Aplica config API  │
       │                  │    (TOTAL OVERRIDE)   │
       │                  └───────────┬───────────┘
       │                              │
       │                              ▼
       │                  ┌───────────────────────┐
       │                  │ 3. Salva cache local  │
       │                  │    (para fallback)    │
       │                  └───────────┬───────────┘
       │                              │
       │                              ├──────────────┐
       │                              │              │
       └─── ❌ API Falha ─────────────┤              │
                                      │              │
                                      ▼              │
                          ┌───────────────────────┐  │
                          │ 4. Tenta cache local  │  │
                          │    (última config OK) │  │
                          └───────────┬───────────┘  │
                                      │              │
                                      ├─── ✅ Tem cache ─┤
                                      │                  │
                                      └─── ❌ Sem cache ─┤
                                                         │
                                                         ▼
                                             ┌───────────────────┐
                                             │ 5. appsettings.json│
                                             │    (FALLBACK FINAL)│
                                             └───────────┬───────┘
                                                         │
                                                         ▼
                                             ┌───────────────────┐
                                             │ 6. Inicia Agent   │
                                             └───────────────────┘
```

---

## 📡 API Contract

### Request: POST /api/agent-config

**Headers:**
```http
Content-Type: application/json
Authorization: Bearer <agent-token>  (opcional, para autenticação)
```

**Body:**
```json
{
  "agentId": "desktop-abc-user123",
  "hostname": "DESKTOP-ABC",
  "username": "user123",
  "version": "1.0.26",
  "osVersion": "Windows 10 Pro 22H2",
  "currentConfig": {
    "version": "v2.3",
    "hash": "abc123..."
  }
}
```

### Response: 200 OK

```json
{
  "agentId": "desktop-abc-user123",
  "configVersion": "v2.5",
  "lastUpdated": "2025-11-14T18:00:00Z",
  "ttl": 3600,

  "mode": "hybrid",

  "recording": {
    "profile": "Performance",
    "overrideProfile": true,
    "continuous": true,
    "intervalMinutes": 60,
    "durationMinutes": 60,
    "segmentSeconds": 30,
    "fps": 30,
    "videoBitrate": 2000,
    "captureAudio": true,
    "codec": "libx264",
    "videoQuality": 23
  },

  "upload": {
    "enabled": true,
    "endpoint": "https://api.example.com/files/",
    "tusServerUrl": "https://tus.example.com/files/",
    "pollIntervalSeconds": 30,
    "maxConcurrentUploads": 2,
    "maxRetries": 3
  },

  "c2": {
    "enabled": true,
    "serverUrl": "https://c2.example.com/api/c2",
    "reconnectDelaySeconds": 5
  },

  "schedule": {
    "enabled": true,
    "timezone": "America/Sao_Paulo",
    "rules": [
      {
        "id": "work-hours",
        "type": "daily",
        "enabled": true,
        "startTime": "08:00",
        "endTime": "18:00",
        "daysOfWeek": ["mon", "tue", "wed", "thu", "fri"],
        "action": "record"
      },
      {
        "id": "weekend-off",
        "type": "daily",
        "enabled": true,
        "daysOfWeek": ["sat", "sun"],
        "action": "pause"
      }
    ]
  },

  "storage": {
    "basePath": "",
    "maxDiskUsageGB": 100,
    "retentionDays": 30
  },

  "webUI": {
    "enabled": true,
    "password": "custom-password-123"
  }
}
```

### Response: 304 Not Modified

Se `currentConfig.version` == versão no servidor, retorna 304 e Agent usa config em cache.

### Response: 404 Not Found

Agent não cadastrado no backend → usar appsettings.json.

### Response: 5xx Server Error

Servidor indisponível → tentar cache → appsettings.json.

---

## 💾 Cache Local

### Estrutura do Arquivo

**Localização:** `{StorageBasePath}/config-cache.json`

**Exemplo:** `C:\Users\Integer\AppData\Local\PaneasMonitor\config-cache.json`

**Conteúdo:**
```json
{
  "fetchedAt": "2025-11-14T18:05:30Z",
  "configVersion": "v2.5",
  "source": "api",
  "config": {
    "mode": "hybrid",
    "recording": { ... },
    "upload": { ... }
  }
}
```

### Invalidação do Cache

- ❌ **Idade > 7 dias:** Considera cache expirado
- ❌ **Source != "api":** Não usar cache de appsettings
- ✅ **API retorna 304:** Cache ainda válido

---

## 🔨 Implementação

### Nova Classe: `Configuration/ConfigurationService.cs`

```csharp
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Agent.Configuration;

/// <summary>
/// Serviço de configuração centralizada com fallback em cascata:
/// 1. API Backend → 2. Cache Local → 3. appsettings.json
/// </summary>
public class ConfigurationService
{
    private readonly ILogger<ConfigurationService> _logger;
    private readonly HttpClient _httpClient;
    private readonly string _cacheFilePath;
    private readonly string _appsettingsPath;

    // Configurações do serviço
    public int ApiTimeoutSeconds { get; set; } = 10;
    public int MaxRetries { get; set; } = 3;
    public int RetryDelayMs { get; set; } = 1000;

    public ConfigurationService(
        string apiBaseUrl,
        string cacheDir,
        string appsettingsPath,
        ILogger<ConfigurationService> logger)
    {
        _logger = logger;
        _appsettingsPath = appsettingsPath;
        _cacheFilePath = Path.Combine(cacheDir, "config-cache.json");

        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(apiBaseUrl),
            Timeout = TimeSpan.FromSeconds(ApiTimeoutSeconds)
        };
    }

    /// <summary>
    /// Carrega configuração com fallback em cascata
    /// </summary>
    public async Task<ConfigManager> LoadConfigurationAsync(
        string agentId,
        string hostname,
        string username,
        CancellationToken ct = default)
    {
        _logger.LogInformation("═══════════════════════════════════════════");
        _logger.LogInformation("  CARREGANDO CONFIGURAÇÃO");
        _logger.LogInformation("═══════════════════════════════════════════");
        _logger.LogInformation("");

        // 1️⃣ PRIORIDADE 1: API Backend
        _logger.LogInformation("[1/3] Tentando buscar configuração da API...");
        var apiConfig = await TryFetchFromApiAsync(agentId, hostname, username, ct);

        if (apiConfig != null)
        {
            _logger.LogInformation("✓ Configuração obtida da API (versão: {Version})", apiConfig.ConfigVersion);

            // Salvar cache para uso offline
            await SaveCacheAsync(apiConfig);

            _logger.LogInformation("");
            _logger.LogInformation("═══════════════════════════════════════════");
            _logger.LogInformation("  CONFIGURAÇÃO: API Backend (✓)");
            _logger.LogInformation("═══════════════════════════════════════════");
            _logger.LogInformation("");

            return apiConfig.ToConfigManager();
        }

        // 2️⃣ PRIORIDADE 2: Cache Local
        _logger.LogWarning("⚠ API indisponível ou falhou");
        _logger.LogInformation("[2/3] Tentando carregar cache local...");

        var cachedConfig = await TryLoadCacheAsync();
        if (cachedConfig != null)
        {
            _logger.LogWarning("⚠ Usando configuração em CACHE (offline mode)");
            _logger.LogInformation("  Versão: {Version}", cachedConfig.ConfigVersion);
            _logger.LogInformation("  Última atualização: {Date}", cachedConfig.FetchedAt);

            _logger.LogInformation("");
            _logger.LogInformation("═══════════════════════════════════════════");
            _logger.LogInformation("  CONFIGURAÇÃO: Cache Local (⚠ OFFLINE)");
            _logger.LogInformation("═══════════════════════════════════════════");
            _logger.LogInformation("");

            return cachedConfig.Config.ToConfigManager();
        }

        // 3️⃣ PRIORIDADE 3: appsettings.json (FALLBACK FINAL)
        _logger.LogWarning("⚠ Cache não disponível");
        _logger.LogInformation("[3/3] Carregando appsettings.json (fallback final)...");

        var fallbackConfig = ConfigManager.LoadFromFile(_appsettingsPath);

        _logger.LogWarning("⚠⚠⚠ USANDO APPSETTINGS.JSON (FALLBACK) ⚠⚠⚠");
        _logger.LogInformation("");
        _logger.LogInformation("═══════════════════════════════════════════");
        _logger.LogInformation("  CONFIGURAÇÃO: appsettings.json (⚠ FALLBACK)");
        _logger.LogInformation("═══════════════════════════════════════════");
        _logger.LogInformation("");

        return fallbackConfig;
    }

    /// <summary>
    /// Tenta buscar configuração da API com retry
    /// </summary>
    private async Task<ApiConfigResponse?> TryFetchFromApiAsync(
        string agentId,
        string hostname,
        string username,
        CancellationToken ct)
    {
        for (int attempt = 1; attempt <= MaxRetries; attempt++)
        {
            try
            {
                var request = new AgentConfigRequest
                {
                    AgentId = agentId,
                    Hostname = hostname,
                    Username = username,
                    Version = GetAssemblyVersion(),
                    OsVersion = Environment.OSVersion.ToString()
                };

                _logger.LogInformation("  Tentativa {Attempt}/{Max}: POST {Endpoint}",
                    attempt, MaxRetries, "/api/agent-config");

                var response = await _httpClient.PostAsJsonAsync("/api/agent-config", request, ct);

                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    _logger.LogWarning("  Agent não cadastrado no backend (404)");
                    return null;
                }

                response.EnsureSuccessStatusCode();

                var config = await response.Content.ReadFromJsonAsync<ApiConfigResponse>(cancellationToken: ct);

                _logger.LogInformation("  ✓ API respondeu com sucesso");
                return config;
            }
            catch (TaskCanceledException)
            {
                _logger.LogWarning("  ✗ Timeout na requisição (tentativa {Attempt}/{Max})", attempt, MaxRetries);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning("  ✗ Erro HTTP: {Message} (tentativa {Attempt}/{Max})", ex.Message, attempt, MaxRetries);
            }
            catch (Exception ex)
            {
                _logger.LogError("  ✗ Erro inesperado: {Message}", ex.Message);
            }

            if (attempt < MaxRetries)
            {
                var delay = RetryDelayMs * attempt; // Backoff progressivo
                _logger.LogInformation("  Aguardando {Delay}ms antes de retry...", delay);
                await Task.Delay(delay, ct);
            }
        }

        return null;
    }

    /// <summary>
    /// Salva configuração em cache local
    /// </summary>
    private async Task SaveCacheAsync(ApiConfigResponse apiConfig)
    {
        try
        {
            var cache = new ConfigCache
            {
                FetchedAt = DateTime.UtcNow,
                ConfigVersion = apiConfig.ConfigVersion ?? "unknown",
                Source = "api",
                Config = apiConfig
            };

            var json = JsonSerializer.Serialize(cache, new JsonSerializerOptions { WriteIndented = true });

            var directory = Path.GetDirectoryName(_cacheFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(_cacheFilePath, json);
            _logger.LogInformation("  ✓ Cache salvo: {Path}", _cacheFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("  ⚠ Falha ao salvar cache: {Message}", ex.Message);
        }
    }

    /// <summary>
    /// Carrega configuração do cache local
    /// </summary>
    private async Task<ConfigCache?> TryLoadCacheAsync()
    {
        try
        {
            if (!File.Exists(_cacheFilePath))
            {
                _logger.LogInformation("  Cache não encontrado: {Path}", _cacheFilePath);
                return null;
            }

            var json = await File.ReadAllTextAsync(_cacheFilePath);
            var cache = JsonSerializer.Deserialize<ConfigCache>(json);

            if (cache == null)
            {
                _logger.LogWarning("  ⚠ Cache inválido (deserialização falhou)");
                return null;
            }

            // Verificar idade do cache (max 7 dias)
            var cacheAge = DateTime.UtcNow - cache.FetchedAt;
            if (cacheAge.TotalDays > 7)
            {
                _logger.LogWarning("  ⚠ Cache expirado (idade: {Days} dias)", cacheAge.TotalDays);
                return null;
            }

            _logger.LogInformation("  ✓ Cache carregado (idade: {Hours}h)", cacheAge.TotalHours);
            return cache;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("  ⚠ Erro ao carregar cache: {Message}", ex.Message);
            return null;
        }
    }

    private string GetAssemblyVersion()
    {
        return System.Reflection.Assembly.GetExecutingAssembly()
            .GetName()
            .Version?
            .ToString() ?? "unknown";
    }
}

// ========== DTOs ==========

public class AgentConfigRequest
{
    public string AgentId { get; set; } = "";
    public string Hostname { get; set; } = "";
    public string Username { get; set; } = "";
    public string Version { get; set; } = "";
    public string OsVersion { get; set; } = "";
}

public class ApiConfigResponse
{
    public string? ConfigVersion { get; set; }
    public DateTime? LastUpdated { get; set; }
    public int? Ttl { get; set; }

    public string? Mode { get; set; }
    public RecordingConfigDto? Recording { get; set; }
    public UploadConfigDto? Upload { get; set; }
    public C2ConfigDto? C2 { get; set; }
    public ScheduleConfigDto? Schedule { get; set; }
    public StorageConfigDto? Storage { get; set; }
    public WebUIConfigDto? WebUI { get; set; }

    public ConfigManager ToConfigManager()
    {
        var config = new ConfigManager();

        if (Mode != null) config.Mode = Mode;

        if (Recording != null)
        {
            config.Recording.Profile = Recording.Profile ?? config.Recording.Profile;
            config.Recording.OverrideProfile = Recording.OverrideProfile ?? config.Recording.OverrideProfile;
            config.Recording.Continuous = Recording.Continuous ?? config.Recording.Continuous;
            config.Recording.IntervalMinutes = Recording.IntervalMinutes ?? config.Recording.IntervalMinutes;
            config.Recording.DurationMinutes = Recording.DurationMinutes ?? config.Recording.DurationMinutes;
            config.Recording.SegmentSeconds = Recording.SegmentSeconds ?? config.Recording.SegmentSeconds;
            config.Recording.FPS = Recording.FPS ?? config.Recording.FPS;
            config.Recording.VideoBitrate = Recording.VideoBitrate ?? config.Recording.VideoBitrate;
            config.Recording.CaptureAudio = Recording.CaptureAudio ?? config.Recording.CaptureAudio;
            config.Recording.Codec = Recording.Codec ?? config.Recording.Codec;
            config.Recording.VideoQuality = Recording.VideoQuality ?? config.Recording.VideoQuality;
        }

        if (Upload != null)
        {
            config.Upload.Enabled = Upload.Enabled ?? config.Upload.Enabled;
            config.Upload.Endpoint = Upload.Endpoint ?? config.Upload.Endpoint;
            config.Upload.PollIntervalSeconds = Upload.PollIntervalSeconds ?? config.Upload.PollIntervalSeconds;
            config.Upload.MaxConcurrentUploads = Upload.MaxConcurrentUploads ?? config.Upload.MaxConcurrentUploads;
            config.Upload.MaxRetries = Upload.MaxRetries ?? config.Upload.MaxRetries;
            config.Upload.ApiKey = Upload.ApiKey ?? config.Upload.ApiKey;
        }

        if (C2 != null)
        {
            config.C2.Enabled = C2.Enabled ?? config.C2.Enabled;
            config.C2.ServerUrl = C2.ServerUrl ?? config.C2.ServerUrl;
            config.C2.ReconnectDelaySeconds = C2.ReconnectDelaySeconds ?? config.C2.ReconnectDelaySeconds;
        }

        // Storage, WebUI, etc...

        return config;
    }
}

public class RecordingConfigDto
{
    public string? Profile { get; set; }
    public bool? OverrideProfile { get; set; }
    public bool? Continuous { get; set; }
    public int? IntervalMinutes { get; set; }
    public int? DurationMinutes { get; set; }
    public int? SegmentSeconds { get; set; }
    public int? FPS { get; set; }
    public int? VideoBitrate { get; set; }
    public bool? CaptureAudio { get; set; }
    public string? Codec { get; set; }
    public int? VideoQuality { get; set; }
}

public class UploadConfigDto
{
    public bool? Enabled { get; set; }
    public string? Endpoint { get; set; }
    public string? TusServerUrl { get; set; }
    public int? PollIntervalSeconds { get; set; }
    public int? MaxConcurrentUploads { get; set; }
    public int? MaxRetries { get; set; }
    public string? ApiKey { get; set; }
}

public class C2ConfigDto
{
    public bool? Enabled { get; set; }
    public string? ServerUrl { get; set; }
    public int? ReconnectDelaySeconds { get; set; }
}

public class ScheduleConfigDto
{
    public bool? Enabled { get; set; }
    public string? Timezone { get; set; }
    public List<ScheduleRuleDto>? Rules { get; set; }
}

public class ScheduleRuleDto
{
    public string? Id { get; set; }
    public string? Type { get; set; }
    public bool? Enabled { get; set; }
    public string? StartTime { get; set; }
    public string? EndTime { get; set; }
    public List<string>? DaysOfWeek { get; set; }
    public string? Action { get; set; }
}

public class StorageConfigDto
{
    public string? BasePath { get; set; }
    public int? MaxDiskUsageGB { get; set; }
    public int? RetentionDays { get; set; }
}

public class WebUIConfigDto
{
    public bool? Enabled { get; set; }
    public string? Password { get; set; }
}

public class ConfigCache
{
    public DateTime FetchedAt { get; set; }
    public string ConfigVersion { get; set; } = "";
    public string Source { get; set; } = ""; // "api" | "appsettings"
    public ApiConfigResponse Config { get; set; } = new();
}
```

---

## 🔧 Modificações no Program.cs

### ANTES:
```csharp
var appConfig = ConfigManager.LoadFromFile();
appConfig.Recording.ApplyProfile();
```

### DEPOIS:
```csharp
// URL da API de configuração (pode vir de variável de ambiente)
string configApiUrl = Environment.GetEnvironmentVariable("CONFIG_API_URL")
    ?? "https://api.paneas.com";

// Criar serviço de configuração
var configService = new ConfigurationService(
    apiBaseUrl: configApiUrl,
    cacheDir: storageBasePath,
    appsettingsPath: "appsettings.json",
    logger: loggerFactory.CreateLogger<ConfigurationService>()
)
{
    ApiTimeoutSeconds = 10,
    MaxRetries = 3,
    RetryDelayMs = 1000
};

// Carregar configuração (API → Cache → appsettings.json)
var appConfig = await configService.LoadConfigurationAsync(
    agentId: config.AgentId,
    hostname: config.Hostname,
    username: Environment.UserName
);

// Aplicar perfil
appConfig.Recording.ApplyProfile();
```

---

## ⏱️ Performance & Timing

### Timeline de Inicialização

#### Cenário 1: API Online (sucesso no 1º attempt)
```
T+0s   → Agent.exe inicia
T+1s   → Tenta buscar config da API
T+1.5s → API responde (200 OK)
T+2s   → Salva cache local
T+2s   → Aplica configuração da API
T+3s   → Inicializa componentes
T+4s   → Inicia gravação
```
**Tempo total:** ~4 segundos

#### Cenário 2: API Offline com Cache
```
T+0s   → Agent.exe inicia
T+1s   → Tenta API (tentativa 1)
T+11s  → Timeout (10s)
T+12s  → Retry 1 (delay 1s)
T+23s  → Timeout (10s)
T+24s  → Retry 2 (delay 2s)
T+35s  → Timeout (10s)
T+36s  → Falha após 3 tentativas
T+36s  → Carrega cache local (sucesso)
T+37s  → Aplica configuração do cache
T+38s  → Inicia gravação
```
**Tempo total:** ~38 segundos (pior caso)

#### Cenário 3: API Offline sem Cache
```
T+36s  → Falha após 3 tentativas API
T+36s  → Cache não encontrado
T+36s  → Carrega appsettings.json
T+37s  → Aplica configuração padrão
T+38s  → Inicia gravação
```
**Tempo total:** ~38 segundos (pior caso)

### Otimizações Recomendadas

Para reduzir tempo de inicialização em caso de falha:
```csharp
configService.ApiTimeoutSeconds = 5;  // Reduzir timeout (default: 10s)
configService.MaxRetries = 2;         // Menos retries (default: 3)
configService.RetryDelayMs = 500;     // Delay menor (default: 1000ms)
```

**Novo tempo com otimização:** ~16 segundos (pior caso)

---

## 🔐 Segurança

### Autenticação

#### Opção 1: Token fixo por Agent
```csharp
_httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {agentToken}");
```

#### Opção 2: Mutual TLS
Cliente e servidor se autenticam via certificados.

#### Opção 3: HMAC Signature
Request assinado com secret compartilhado.

### Validação de Schema

```csharp
// Validar response antes de aplicar
if (apiConfig.Recording?.SegmentSeconds < 0)
{
    _logger.LogWarning("Config inválida: SegmentSeconds negativo");
    return null; // Rejeitar config malformada
}
```

### HTTPS Obrigatório

```csharp
if (!_httpClient.BaseAddress.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException("API deve usar HTTPS em produção");
}
```

---

## 📊 Monitoramento & Telemetria

### Logs Importantes

```csharp
_logger.LogInformation("Config carregada de: {Source}", source);  // api | cache | appsettings
_logger.LogInformation("Config version: {Version}", configVersion);
_logger.LogInformation("Config age: {Age}", cacheAge);
```

### Métricas Backend

O backend deve rastrear:
- ✅ Quantos agents buscaram config (por dia/hora)
- ✅ Versões de Agent em uso
- ✅ Agents que estão com configs antigas
- ✅ Taxa de sucesso/falha das requisições
- ✅ Tempo médio de resposta

### Alertas

- 🔔 Agent não buscou config há > 7 dias
- 🔔 Versão de Agent < mínima suportada
- 🔔 Taxa de erro da API > 5%

---

## 🧪 Testes

### Casos de Teste

1. **API online e respondendo:**
   - Deve usar config da API
   - Deve salvar cache

2. **API retorna 404:**
   - Agent não cadastrado
   - Deve usar appsettings.json

3. **API timeout:**
   - Deve fazer retry
   - Após N falhas, usar cache
   - Se sem cache, usar appsettings.json

4. **API retorna config malformada:**
   - Deve rejeitar
   - Deve usar cache ou appsettings.json

5. **Cache expirado (> 7 dias):**
   - Deve ignorar cache antigo
   - Deve usar appsettings.json

6. **Primeira inicialização (sem cache):**
   - API online: usar API
   - API offline: usar appsettings.json

---

## 📝 Checklist de Implementação

### Fase 1: Core
- [ ] Criar `ConfigurationService.cs`
- [ ] Criar DTOs (Request/Response)
- [ ] Implementar TryFetchFromApiAsync()
- [ ] Implementar cache local (save/load)
- [ ] Modificar Program.cs

### Fase 2: Robustez
- [ ] Retry com backoff exponencial
- [ ] Validação de schema da response
- [ ] Timeout configurável
- [ ] Logs estruturados

### Fase 3: Segurança
- [ ] HTTPS enforcement
- [ ] Autenticação (Bearer token)
- [ ] Validação de certificado

### Fase 4: Testes
- [ ] Testes unitários (ConfigurationService)
- [ ] Testes de integração (API mock)
- [ ] Testes de fallback (API offline)
- [ ] Testes de cache

### Fase 5: Deploy
- [ ] Backend API implementado
- [ ] Documentação da API
- [ ] Variável de ambiente CONFIG_API_URL
- [ ] Rollout gradual (canary deployment)

---

## 🚀 Roadmap Futuro

### v2.0: Hot-Reload via API
- Agent verifica periodicamente se config mudou (polling)
- Aplica nova config sem reiniciar
- Webhook para push de updates

### v3.0: Feature Flags
- Backend controla features habilitadas/desabilitadas
- A/B testing granular
- Rollout gradual de features

### v4.0: Multi-Tenant
- Suporte a múltiplas organizações
- Configs por tenant/grupo/user
- RBAC (role-based access control)

---

## 📚 Referências

- **Spring Cloud Config:** https://spring.io/projects/spring-cloud-config
- **Consul:** https://www.consul.io/
- **etcd:** https://etcd.io/
- **Azure App Configuration:** https://azure.microsoft.com/services/app-configuration/

---

## ✅ Conclusão

A arquitetura proposta permite:

1. ✅ **Gestão centralizada** de configurações
2. ✅ **Fallback robusto** (API → Cache → appsettings.json)
3. ✅ **Operação offline** via cache local
4. ✅ **Escalabilidade** para milhares de agents
5. ✅ **Flexibilidade** para experimentação
6. ✅ **Auditoria** e rastreabilidade

**Recomendação:** Implementar em fases, começando pelo core (Fase 1) e expandindo incrementalmente.
