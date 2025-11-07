# Deployment: Service + Task Scheduler Hybrid

**Data:** 2025-11-07
**Status:** Planejamento
**Complexidade:** Alta (4-6 dias)
**Proteção:** ⭐⭐⭐⭐☆ (Usuário Comum) | ⭐⭐☆☆☆ (Admin)

---

## Visão Geral

Esta abordagem combina **Windows Service** (Session 0) com **Task Scheduler** (User Session) para criar um sistema de monitoramento robusto, auto-recuperável e resistente a tentativas de desabilitação.

### Conceito Principal

```
Windows Service (Session 0)
    ├─ Monitora Task Scheduler
    ├─ Recria task se deletada
    ├─ Força execução se Agent não está rodando
    └─ Roda desde boot (antes de qualquer login)

Task Scheduler (User Session)
    ├─ Detecta login do usuário
    ├─ Inicia Agent.exe na sessão do usuário
    └─ Gerenciado e protegido pelo Service

Agent.exe (User Session)
    ├─ Grava tela e áudio
    ├─ Upload de vídeos
    └─ Roda naturalmente na sessão do usuário
```

---

## Arquitetura Detalhada

```
┌─────────────────────────────────────────────────────────┐
│         Windows Service (Session 0)                     │
│         "PaneasMonitorService"                          │
│                                                          │
│  Funções:                                               │
│  • Garante que Task Scheduler está configurado         │
│  • Re-cria task se for deletado                        │
│  • Monitora se Agent.exe está rodando                  │
│  • Força execução da task se necessário                │
│  • Roda como LocalSystem ou NetworkService             │
│                                                          │
│  Loop de Monitoramento (a cada 10 segundos):           │
│  1. Verificar se task existe                            │
│  2. Verificar se task está habilitada                  │
│  3. Verificar se há sessão de usuário ativa            │
│  4. Verificar se Agent.exe está rodando nessa sessão   │
│  5. Se não está rodando → Forçar execução da task      │
│                                                          │
└────────────────┬────────────────────────────────────────┘
                 │ monitora e protege
                 ↓
┌─────────────────────────────────────────────────────────┐
│      Task Scheduler (Trigger: User Logon)              │
│      Task: "\Microsoft\Windows\Paneas\Monitor"         │
│                                                          │
│  Configuração:                                          │
│  • Trigger: "At log on of any user"                    │
│  • Action: Execute Agent.exe                           │
│  • Hidden: true (oculto da UI)                         │
│  • AllowHardTerminate: false                           │
│  • MultipleInstancesPolicy: IgnoreNew                  │
│                                                          │
│  Funções:                                               │
│  • Detecta quando qualquer usuário faz login           │
│  • Inicia Agent.exe na sessão do usuário               │
│  • Gerenciado pelo Windows Task Scheduler              │
│                                                          │
└────────────────┬────────────────────────────────────────┘
                 │ lança no login
                 ↓
┌─────────────────────────────────────────────────────────┐
│           Agent.exe (User Session)                      │
│           Processo Principal de Gravação                │
│                                                          │
│  Características:                                       │
│  • Roda na sessão do usuário (Session ID > 0)          │
│  • Tem acesso ao desktop do usuário                    │
│  • Pode capturar tela via FFmpeg gdigrab               │
│  • Pode capturar áudio via NAudio WASAPI               │
│  • Não precisa de CreateProcessAsUser                  │
│                                                          │
│  Workers Ativos:                                        │
│  • VideoRecorderWorker (gravação contínua)             │
│  • UploadWorker (upload de vídeos)                     │
│  • C2ClientWorker (comunicação com servidor)           │
│                                                          │
└─────────────────────────────────────────────────────────┘
```

---

## Cenários de Operação

### Cenário 1: Inicialização Normal

```
┌─────────────────────────────────────────────────────────┐
│ BOOT                                                     │
└─────────────────────────────────────────────────────────┘
         │
         ↓
┌─────────────────────────────────────────────────────────┐
│ Windows Service "PaneasMonitorService" inicia           │
│ (antes de qualquer usuário fazer login)                 │
└─────────────────────────────────────────────────────────┘
         │
         ↓
┌─────────────────────────────────────────────────────────┐
│ Service verifica Task Scheduler:                        │
│ • Task existe? Se NÃO → Cria                           │
│ • Task habilitada? Se NÃO → Habilita                   │
└─────────────────────────────────────────────────────────┘
         │
         ↓
┌─────────────────────────────────────────────────────────┐
│ USUÁRIO FAZ LOGIN                                       │
└─────────────────────────────────────────────────────────┘
         │
         ↓
┌─────────────────────────────────────────────────────────┐
│ Task Scheduler detecta login e executa Agent.exe        │
└─────────────────────────────────────────────────────────┘
         │
         ↓
┌─────────────────────────────────────────────────────────┐
│ Agent.exe inicia na sessão do usuário                   │
│ • Conecta ao C2 server                                  │
│ • Inicia gravação de vídeo (se configurado)            │
└─────────────────────────────────────────────────────────┘
         │
         ↓
┌─────────────────────────────────────────────────────────┐
│ Service monitora continuamente (loop de 10s):          │
│ • Task ainda existe? ✓                                  │
│ • Agent.exe está rodando? ✓                            │
└─────────────────────────────────────────────────────────┘
```

**Tempo até Agent rodar:** ~2-5 segundos após login

---

### Cenário 2: Usuário Comum Tenta Matar Agent.exe

```
┌─────────────────────────────────────────────────────────┐
│ USUÁRIO: Abre Task Manager                             │
│ USUÁRIO: Seleciona Agent.exe → "End Task"              │
└─────────────────────────────────────────────────────────┘
         │
         ↓
┌─────────────────────────────────────────────────────────┐
│ Agent.exe é terminado (processo morre)                  │
└─────────────────────────────────────────────────────────┘
         │
         ↓ (até 10 segundos)
┌─────────────────────────────────────────────────────────┐
│ Service detecta: Agent.exe não está rodando             │
└─────────────────────────────────────────────────────────┘
         │
         ↓
┌─────────────────────────────────────────────────────────┐
│ Service força execução da Task Scheduler:               │
│ task.Run(null); // COM API                              │
└─────────────────────────────────────────────────────────┘
         │
         ↓
┌─────────────────────────────────────────────────────────┐
│ Agent.exe reinicia imediatamente                        │
└─────────────────────────────────────────────────────────┘
         │
         ↓
┌─────────────────────────────────────────────────────────┐
│ USUÁRIO: Vê o processo de volta no Task Manager        │
│ "Por que voltou?" 🤔                                    │
└─────────────────────────────────────────────────────────┘
```

**Tempo de recuperação:** ~10 segundos (intervalo de monitoramento do Service)

**Resultado:** ✅ Agent volta automaticamente, usuário não consegue desabilitar permanentemente

---

### Cenário 3: Usuário/Admin Tenta Desabilitar Task

```
┌─────────────────────────────────────────────────────────┐
│ USUÁRIO/ADMIN: Abre Task Scheduler (taskschd.msc)      │
│ Navega até: \Microsoft\Windows\Paneas\Monitor          │
│ Ação: "Disable" ou "Delete"                            │
└─────────────────────────────────────────────────────────┘
         │
         ↓
┌─────────────────────────────────────────────────────────┐
│ Task é desabilitada/deletada                            │
└─────────────────────────────────────────────────────────┘
         │
         ↓ (até 10 segundos)
┌─────────────────────────────────────────────────────────┐
│ Service detecta:                                         │
│ • Task não existe OU                                    │
│ • Task existe mas está desabilitada                     │
└─────────────────────────────────────────────────────────┘
         │
         ↓
┌─────────────────────────────────────────────────────────┐
│ Service RECRIA/HABILITA a task automaticamente          │
│ CreateScheduledTask() // método do Service              │
└─────────────────────────────────────────────────────────┘
         │
         ↓
┌─────────────────────────────────────────────────────────┐
│ Service verifica: Agent.exe está rodando?               │
│ Se NÃO → Força execução da task                        │
└─────────────────────────────────────────────────────────┘
         │
         ↓
┌─────────────────────────────────────────────────────────┐
│ USUÁRIO/ADMIN: Atualiza Task Scheduler                 │
│ "A task voltou!" 😲                                     │
└─────────────────────────────────────────────────────────┘
```

**Tempo de recuperação:** ~10 segundos (self-healing automático)

**Resultado:** ✅ Task é recriada/habilitada automaticamente, sistema continua funcionando

---

### Cenário 4: Admin Tenta Parar o Service

```
┌─────────────────────────────────────────────────────────┐
│ ADMIN: Abre Services.msc (Services Console)            │
│ Localiza: "PaneasMonitorService"                       │
│ Ação: "Stop" (parar service)                           │
└─────────────────────────────────────────────────────────┘
         │
         ↓
┌─────────────────────────────────────────────────────────┐
│ Service para (ExecuteAsync cancela)                     │
└─────────────────────────────────────────────────────────┘
         │
         ↓
┌─────────────────────────────────────────────────────────┐
│ Agent.exe continua rodando normalmente                  │
│ (já estava na sessão do usuário, independente)          │
└─────────────────────────────────────────────────────────┘
         │
         ↓ (após 1 minuto - Recovery config)
┌─────────────────────────────────────────────────────────┐
│ Windows Recovery Policy detecta falha do service        │
│ Action: "Restart the service"                           │
└─────────────────────────────────────────────────────────┘
         │
         ↓
┌─────────────────────────────────────────────────────────┐
│ Service reinicia automaticamente                        │
└─────────────────────────────────────────────────────────┘
         │
         ↓
┌─────────────────────────────────────────────────────────┐
│ Service volta a monitorar:                              │
│ • Verifica task                                         │
│ • Verifica Agent.exe                                    │
│ • Sistema volta ao normal                              │
└─────────────────────────────────────────────────────────┘
```

**Tempo de recuperação:** ~60 segundos (configurado na Recovery tab)

**Resultado:** ⚠️ Service reinicia automaticamente, mas há gap de ~1 minuto sem proteção

---

### Cenário 5: Admin Ataque Completo (Múltiplos Passos)

```
┌─────────────────────────────────────────────────────────┐
│ ADMIN DETERMINADO: Quer desabilitar completamente       │
└─────────────────────────────────────────────────────────┘
         │
         ↓
┌─────────────────────────────────────────────────────────┐
│ Passo 1: Para o service                                 │
│ sc.exe stop "PaneasMonitorService"                      │
└─────────────────────────────────────────────────────────┘
         │
         ↓
┌─────────────────────────────────────────────────────────┐
│ Passo 2: Desabilita auto-start do service               │
│ sc.exe config "PaneasMonitorService" start= disabled    │
└─────────────────────────────────────────────────────────┘
         │
         ↓
┌─────────────────────────────────────────────────────────┐
│ Passo 3: Deleta a task do Task Scheduler                │
│ schtasks /Delete /TN "\Microsoft\Windows\Paneas\Monitor"│
└─────────────────────────────────────────────────────────┘
         │
         ↓
┌─────────────────────────────────────────────────────────┐
│ Passo 4: Mata o processo Agent.exe                      │
│ taskkill /F /IM Agent.exe                               │
└─────────────────────────────────────────────────────────┘
         │
         ↓
┌─────────────────────────────────────────────────────────┐
│ SISTEMA DESABILITADO                                    │
│ ❌ Service não vai reiniciar (disabled)                │
│ ❌ Task não existe                                      │
│ ❌ Agent não está rodando                               │
└─────────────────────────────────────────────────────────┘
         │
         ↓
┌─────────────────────────────────────────────────────────┐
│ PRÓXIMO REBOOT:                                         │
│ ❌ Service não inicia (startup disabled)               │
│ Sistema permanece desabilitado                          │
└─────────────────────────────────────────────────────────┘
```

**Resultado:** ❌ Admin venceu (requer 4 passos conscientes e conhecimento técnico)

**Nota:** Isso deixa rastros claros em logs do sistema (Event Viewer, Security logs)

---

## Níveis de Proteção

### Contra Usuário Comum (⭐⭐⭐⭐☆)

| Tentativa | Requer Admin? | Resultado | Tempo Recuperação |
|-----------|---------------|-----------|-------------------|
| Matar Agent.exe no Task Manager | ❌ Não | ✅ Reinicia automaticamente | ~10 segundos |
| Desabilitar Task Scheduler task | ❌ Não (HKCU) / ✅ Sim (HKLM) | ✅ Task é habilitada automaticamente | ~10 segundos |
| Deletar Task Scheduler task | ✅ Sim | ✅ Task é recriada automaticamente | ~10 segundos |
| Parar Windows Service | ✅ Sim | N/A (usuário comum não consegue) | N/A |

**Conclusão:** Usuário comum **não consegue** desabilitar permanentemente o sistema.

### Contra Admin (⭐⭐☆☆☆)

| Tentativa | Passos Necessários | Resultado | Recuperação |
|-----------|-------------------|-----------|-------------|
| Matar Agent.exe | 1 passo | ✅ Reinicia automaticamente | ~10 segundos |
| Desabilitar task | 1 passo | ✅ Habilitada automaticamente | ~10 segundos |
| Deletar task | 1 passo | ✅ Recriada automaticamente | ~10 segundos |
| Parar service | 1 passo | ⚠️ Reinicia automaticamente | ~60 segundos |
| Parar + desabilitar service | 2 passos | ⚠️ Não reinicia até reboot | Permanente |
| Ataque completo (4 passos) | 4 passos | ❌ Sistema desabilitado | Permanente |

**Conclusão:** Admin **pode** desabilitar, mas requer **múltiplos passos** e **conhecimento técnico**. Não é um simples "End Task".

---

## Componentes Necessários

### 1. Windows Service: PaneasMonitorService

**Tipo:** .NET 8.0 BackgroundService (Windows Service)

**Responsabilidades:**
- Monitorar Task Scheduler task
- Recriar task se deletada
- Habilitar task se desabilitada
- Monitorar processos Agent.exe na sessão ativa
- Forçar execução da task se necessário
- Logging de eventos

**Tecnologias:**
- `Microsoft.Extensions.Hosting.WindowsServices` (já existe no projeto)
- COM Interop com Task Scheduler (`TaskScheduler.TaskScheduler`)
- P/Invoke para sessões Windows (WTS APIs)

**Estrutura do Código:**

```csharp
// PaneasMonitorService/MonitorService.cs
public class MonitorService : BackgroundService
{
    private readonly ILogger<MonitorService> _logger;
    private TaskScheduler.TaskScheduler? _taskScheduler;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PaneasMonitorService iniciado");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // 1. Garantir que task existe e está habilitada
                EnsureTaskExistsAndEnabled();

                // 2. Verificar se Agent.exe está rodando na sessão ativa
                if (IsUserSessionActive() && !IsAgentRunning())
                {
                    _logger.LogWarning("Agent.exe não está rodando. Forçando execução...");
                    ForceRunTask();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro no loop de monitoramento");
            }

            await Task.Delay(10000, stoppingToken); // Verificar a cada 10 segundos
        }
    }

    private void EnsureTaskExistsAndEnabled()
    {
        // Conectar ao Task Scheduler via COM
        // Verificar se task existe
        // Se não existe: CreateScheduledTask()
        // Se existe mas desabilitada: task.Enabled = true
    }

    private bool IsUserSessionActive()
    {
        uint sessionId = WTSGetActiveConsoleSessionId();
        return sessionId != 0xFFFFFFFF; // 0xFFFFFFFF = nenhuma sessão ativa
    }

    private bool IsAgentRunning()
    {
        uint sessionId = WTSGetActiveConsoleSessionId();
        return Process.GetProcessesByName("Agent")
            .Any(p => GetProcessSessionId(p.Id) == sessionId);
    }

    private void ForceRunTask()
    {
        // Usar COM API para executar task manualmente
        // task.Run(null);
    }
}
```

---

### 2. Task Scheduler Task Definition

**Arquivo:** `PaneasMonitor.xml`

```xml
<?xml version="1.0" encoding="UTF-16"?>
<Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
  <RegistrationInfo>
    <Description>Paneas Monitor - Autonomous Recording Agent</Description>
    <URI>\Microsoft\Windows\Paneas\Monitor</URI>
  </RegistrationInfo>

  <Triggers>
    <LogonTrigger>
      <Enabled>true</Enabled>
      <UserId>BUILTIN\Users</UserId> <!-- Qualquer usuário -->
    </LogonTrigger>
  </Triggers>

  <Principals>
    <Principal>
      <LogonType>InteractiveToken</LogonType>
      <RunLevel>LeastPrivilege</RunLevel> <!-- Roda com privilégios do usuário -->
    </Principal>
  </Principals>

  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>false</AllowHardTerminate>
    <StartWhenAvailable>true</StartWhenAvailable>
    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
    <Hidden>true</Hidden> <!-- Oculto da Task Scheduler UI -->
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit> <!-- Sem limite de tempo -->
    <Priority>4</Priority> <!-- Prioridade normal -->
  </Settings>

  <Actions>
    <Exec>
      <Command>C:\Program Files\PaneasMonitor\Agent.exe</Command>
      <WorkingDirectory>C:\Program Files\PaneasMonitor</WorkingDirectory>
    </Exec>
  </Actions>
</Task>
```

---

### 3. Agent.exe (Já Existe)

**Mudanças Necessárias:** Mínimas

**Melhorias Opcionais:**
1. **Mutex para evitar múltiplas instâncias:**
```csharp
// Program.cs
using Mutex _mutex = new Mutex(true, "Global\\PaneasMonitorAgent", out bool createdNew);
if (!createdNew)
{
    Console.WriteLine("Agent já está rodando. Encerrando...");
    return;
}
```

2. **Esconder janela de console (opcional):**
```csharp
// Agent.csproj
<PropertyGroup>
  <OutputType>WinExe</OutputType> <!-- Em vez de Exe -->
</PropertyGroup>
```

---

### 4. Scripts de Instalação

#### install.ps1 (Executar como Admin)

```powershell
#Requires -RunAsAdministrator

$ErrorActionPreference = "Stop"

Write-Host "=== Paneas Monitor - Instalação ===" -ForegroundColor Cyan

# Caminhos
$installPath = "C:\Program Files\PaneasMonitor"
$servicePath = "$installPath\PaneasMonitorService.exe"
$agentPath = "$installPath\Agent.exe"

# 1. Criar diretório de instalação
Write-Host "Criando diretório de instalação..." -ForegroundColor Yellow
New-Item -ItemType Directory -Path $installPath -Force | Out-Null

# 2. Copiar arquivos
Write-Host "Copiando arquivos..." -ForegroundColor Yellow
Copy-Item -Path ".\publish\*" -Destination $installPath -Recurse -Force

# 3. Instalar Windows Service
Write-Host "Instalando Windows Service..." -ForegroundColor Yellow
sc.exe create "PaneasMonitorService" `
    binPath= $servicePath `
    start= auto `
    DisplayName= "Paneas Monitor Service" `
    depend= "TaskScheduler"

# 4. Configurar Recovery (restart automático)
Write-Host "Configurando recovery policy..." -ForegroundColor Yellow
sc.exe failure "PaneasMonitorService" `
    reset= 86400 `
    actions= restart/60000/restart/60000/restart/60000

# 5. Iniciar service (ele criará a task automaticamente)
Write-Host "Iniciando service..." -ForegroundColor Yellow
sc.exe start "PaneasMonitorService"

Write-Host "=== Instalação concluída! ===" -ForegroundColor Green
Write-Host "Service iniciado. Task será criada automaticamente." -ForegroundColor Green
```

#### uninstall.ps1 (Executar como Admin)

```powershell
#Requires -RunAsAdministrator

$ErrorActionPreference = "Stop"

Write-Host "=== Paneas Monitor - Desinstalação ===" -ForegroundColor Cyan

# 1. Parar e remover service
Write-Host "Parando e removendo service..." -ForegroundColor Yellow
sc.exe stop "PaneasMonitorService" 2>$null
sc.exe delete "PaneasMonitorService" 2>$null

# 2. Deletar task
Write-Host "Removendo scheduled task..." -ForegroundColor Yellow
schtasks /Delete /TN "\Microsoft\Windows\Paneas\Monitor" /F 2>$null

# 3. Matar processos
Write-Host "Encerrando processos..." -ForegroundColor Yellow
Stop-Process -Name "Agent" -Force -ErrorAction SilentlyContinue
Stop-Process -Name "PaneasMonitorService" -Force -ErrorAction SilentlyContinue

# 4. Remover arquivos
Write-Host "Removendo arquivos..." -ForegroundColor Yellow
Remove-Item -Path "C:\Program Files\PaneasMonitor" -Recurse -Force -ErrorAction SilentlyContinue

Write-Host "=== Desinstalação concluída! ===" -ForegroundColor Green
```

---

## Timeline de Implementação

### Fase 1: Service Foundation (Dias 1-2)

**Dia 1:**
- ✅ Criar projeto `PaneasMonitorService`
- ✅ Implementar `BackgroundService` básico
- ✅ Adicionar loop de monitoramento (10s interval)
- ✅ Testar instalação/desinstalação do service

**Dia 2:**
- ✅ Implementar detecção de sessão ativa (WTS APIs)
- ✅ Implementar detecção de processo Agent.exe
- ✅ Adicionar logging (EventLog + FileLog)
- ✅ Testar monitoramento básico

### Fase 2: Task Scheduler Integration (Dia 3)

**Dia 3:**
- ✅ Adicionar COM Interop com Task Scheduler
- ✅ Implementar `EnsureTaskExistsAndEnabled()`
- ✅ Implementar `CreateScheduledTask()`
- ✅ Implementar `ForceRunTask()`
- ✅ Criar `PaneasMonitor.xml` task definition
- ✅ Testar criação/recriação de task

### Fase 3: Integration Testing (Dias 4-5)

**Dia 4:**
- ✅ Testar cenário 1: Inicialização normal
- ✅ Testar cenário 2: Kill Agent.exe
- ✅ Testar cenário 3: Desabilitar task
- ✅ Testar cenário 4: Parar service
- ✅ Identificar e corrigir bugs

**Dia 5:**
- ✅ Testar múltiplos usuários (Fast User Switching)
- ✅ Testar Remote Desktop (RDP)
- ✅ Testar lock/unlock
- ✅ Testar reinicialização do sistema
- ✅ Stress testing (kill repetido, etc.)

### Fase 4: Polish & Deploy (Dia 6)

**Dia 6:**
- ✅ Refinar scripts de instalação/desinstalação
- ✅ Adicionar diagnósticos e logging detalhado
- ✅ Criar documentação de deployment
- ✅ Preparar build de release
- ✅ Deploy testing em ambiente real

---

## Comparação com Outras Abordagens

| Critério | Service+Task (5) | Task Only (2) | Watchdog (3) | Service Only (1) |
|----------|------------------|---------------|--------------|------------------|
| **Proteção Usuário Comum** | ⭐⭐⭐⭐☆ | ⭐⭐☆☆☆ | ⭐⭐☆☆☆ | ⭐⭐⭐⭐⭐ |
| **Proteção Admin** | ⭐⭐☆☆☆ | ⭐☆☆☆☆ | ⭐☆☆☆☆ | ⭐⭐⭐☆☆ |
| **Self-Healing** | ✅ Automático | ❌ Manual | ✅ Watchdog | ❌ Manual |
| **Restart Rápido** | ⭐⭐⭐⭐⭐ (~10s) | ⭐⭐⭐☆☆ (variável) | ⭐⭐⭐⭐☆ (imediato) | ⭐⭐⭐⭐⭐ (automático) |
| **User Session Native** | ✅ Sim | ✅ Sim | ✅ Sim | ❌ Requer CreateProcessAsUser |
| **Complexidade** | Alta (4-6 dias) | Baixa (8h) | Média (1-2 dias) | Alta (3-5 dias) |
| **Invisibilidade** | ⭐⭐⭐⭐☆ | ⭐⭐⭐☆☆ | ⭐⭐⭐☆☆ | ⭐⭐⭐⭐⭐ |
| **Confiabilidade** | ⭐⭐⭐⭐⭐ | ⭐⭐⭐☆☆ | ⭐⭐⭐⭐☆ | ⭐⭐⭐⭐☆ |
| **Manutenção** | Média | Baixa | Baixa | Alta |

---

## Vantagens

✅ **Proteção em Camadas:** Service protege Task, Task lança Agent
✅ **Self-Healing Automático:** Recria task se deletada
✅ **Restart Rápido:** ~10 segundos após kill
✅ **User Session Nativo:** Não precisa de CreateProcessAsUser
✅ **Service-Level Protection:** Usuário comum não pode parar
✅ **Reliable:** Múltiplos pontos de falha precisam falhar juntos
✅ **Professional:** Solução enterprise-grade

---

## Desvantagens

❌ **Alta Complexidade:** 4-6 dias de desenvolvimento
❌ **Múltiplos Componentes:** Service + Task + Agent
❌ **Admin Pode Vencer:** Requer múltiplos passos mas é possível
❌ **Manutenção:** Duas camadas de sistema para manter
❌ **Recovery Gap:** ~60s se service for parado manualmente

---

## Quando Usar Esta Solução

### ✅ USE SE:
- Precisa de **máxima confiabilidade** em produção
- Quer **proteção contra usuário comum**
- Pode investir **4-6 dias** de desenvolvimento
- Deployment **profissional/empresarial**
- Precisa de **self-healing automático**

### ❌ NÃO USE SE:
- Precisa de algo **rápido** (< 2 dias) → Use **Task Scheduler (2)**
- É apenas **POC/teste** → Use **Task Scheduler (2)**
- Orçamento/tempo limitado → Use **Watchdog (3)**
- Precisa proteção **absoluta** contra admin → Não existe solução perfeita

---

## Considerações de Segurança

### ⚠️ AVISOS IMPORTANTES

Este sistema tem características de malware:
- ✅ Auto-start persistente
- ✅ Resistente a desabilitação
- ✅ Operação oculta (hidden task)
- ✅ Gravação de tela/áudio
- ✅ Difícil de remover

### Requisitos Legais

Antes de deployment:
1. ✅ **Consentimento do usuário** - Obrigatório por escrito
2. ✅ **Compliance legal** - GDPR, CCPA, LGPD, etc.
3. ✅ **Disclosure** - Usuário deve saber que está sendo monitorado
4. ✅ **Easy removal** - Fornecer uninstall.ps1
5. ✅ **Data protection** - Criptografia e storage seguro

### Safeguards Recomendados

**1. Indicador Visual:**
```csharp
// Mostrar ícone na system tray
NotifyIcon trayIcon = new NotifyIcon();
trayIcon.Icon = SystemIcons.Shield;
trayIcon.Text = "Paneas Monitor - Recording Active";
trayIcon.Visible = true;
```

**2. User Control:**
- Adicionar "Pause Monitoring"
- Adicionar "Stop Monitoring"
- Notification antes de iniciar gravação

**3. Audit Trail:**
- Log todas as operações
- Timestamp de gravações
- Access logs

---

## Próximos Passos

Para implementar esta solução:

1. **Criar nova branch:**
   ```bash
   git checkout -b feature/service-task-hybrid
   ```

2. **Criar projeto Service:**
   ```bash
   cd c2-agent
   dotnet new worker -n PaneasMonitorService
   ```

3. **Seguir timeline de implementação** (6 dias)

4. **Testar extensivamente** antes de deploy

---

## Referências

- [Windows Services Documentation](https://docs.microsoft.com/en-us/dotnet/core/extensions/windows-service)
- [Task Scheduler COM API](https://docs.microsoft.com/en-us/windows/win32/taskschd/task-scheduler-start-page)
- [Windows Terminal Services API](https://docs.microsoft.com/en-us/windows/win32/termserv/terminal-services-functions)
- [Service Recovery Actions](https://docs.microsoft.com/en-us/windows/win32/services/service-recovery)

---

**Documento criado em:** 2025-11-07
**Status:** Planejamento
**Próxima revisão:** Após implementação (Dia 6)
