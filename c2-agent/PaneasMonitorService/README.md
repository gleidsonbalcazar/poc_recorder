# PaneasMonitorService

Windows Service que monitora e protege o agente Paneas Monitor, garantindo que ele esteja sempre em execução na sessão ativa do usuário.

## Arquitetura

Este serviço implementa a abordagem **Service + Task Scheduler Hybrid**:

- **Windows Service** (Session 0): Monitora continuamente o estado do sistema
- **Task Scheduler**: Executa o Agent.exe na sessão do usuário logado
- **Auto-Recovery**: Detecta e corrige problemas automaticamente

### Como Funciona

1. Serviço roda como SYSTEM na Session 0 (background)
2. A cada 10 segundos, verifica:
   - Se há uma sessão de usuário ativa
   - Se a Task Scheduler existe e está habilitada
   - Se Agent.exe está rodando na sessão ativa
3. Se detectar problemas, corrige automaticamente:
   - Recria task se foi deletada
   - Reabilita task se foi desabilitada
   - Executa task se Agent.exe não está rodando

## Configuração

Edite `appsettings.json`:

```json
{
  "Service": {
    "MonitorIntervalSeconds": 10,
    "AgentExecutablePath": "C:\\Program Files\\Paneas Monitor\\bin\\Agent.exe",
    "TaskName": "PaneasMonitorTask",
    "TaskXmlPath": "C:\\Program Files\\Paneas Monitor\\config\\task-definition.xml"
  }
}
```

## Build

```powershell
# Debug
dotnet build

# Release
dotnet build -c Release
```

## Scripts Auxiliares

Todos os scripts devem ser executados **como Administrador**.

### 1. install-service.ps1

Instala o serviço no sistema.

```powershell
cd Scripts
.\install-service.ps1
```

O que faz:
- Compila o projeto (Release)
- Instala o serviço usando sc.exe
- Configura auto-start
- Configura recovery (restart on failure)
- Inicia o serviço

### 2. uninstall-service.ps1

Remove o serviço do sistema.

```powershell
cd Scripts
.\uninstall-service.ps1
```

O que faz:
- Para o serviço (se estiver rodando)
- Remove o serviço usando sc.exe

### 3. test-console.ps1

Executa o serviço como aplicação de console para debugging.

```powershell
# Debug mode
cd Scripts
.\test-console.ps1

# Release mode
.\test-console.ps1 -Release
```

O que faz:
- Compila o projeto
- Executa o .exe diretamente (não como serviço)
- Mostra logs em tempo real no console
- Útil para debugging antes de instalar como serviço

**Nota:** Pressione `Ctrl+C` para parar.

### 4. debug-scenarios.ps1

Menu interativo para testar diferentes cenários de ataque/recuperação.

```powershell
cd Scripts
.\debug-scenarios.ps1
```

Opções disponíveis:

**Task Scheduler:**
1. Verificar Task Scheduler (ver se task existe)
2. Desabilitar Task Scheduler (simular ataque)
3. Habilitar Task Scheduler
4. Deletar Task Scheduler (simular remoção)
5. Executar Task Scheduler manualmente

**Agent.exe:**
6. Verificar processo Agent.exe
7. Matar processo Agent.exe (simular kill)

**Sistema:**
8. Verificar sessão ativa do usuário
9. Verificar serviço PaneasMonitorService

## Workflow de Testes

### Teste Básico (Primeira Vez)

1. **Compile o projeto:**
   ```powershell
   dotnet build -c Release
   ```

2. **Teste em modo console primeiro:**
   ```powershell
   cd Scripts
   .\test-console.ps1 -Release
   ```
   - Verifique se não há erros
   - Veja se os logs aparecem corretamente
   - Pressione Ctrl+C para parar

3. **Instale como serviço:**
   ```powershell
   .\install-service.ps1
   ```

4. **Verifique se está rodando:**
   ```powershell
   Get-Service PaneasMonitorService
   ```

5. **Veja os logs no Event Viewer:**
   ```powershell
   Get-EventLog -LogName Application -Source PaneasMonitorService -Newest 20
   ```

### Teste de Cenários de Recuperação

Use o `debug-scenarios.ps1` para testar a proteção:

#### Cenário 1: Desabilitar Task
```powershell
.\debug-scenarios.ps1
# Escolha opção 2: Desabilitar Task Scheduler
# Aguarde ~10 segundos
# Escolha opção 1: Verificar Task Scheduler
# Resultado esperado: Task deve estar habilitada novamente
```

#### Cenário 2: Deletar Task
```powershell
.\debug-scenarios.ps1
# Escolha opção 4: Deletar Task Scheduler
# Aguarde ~10 segundos
# Escolha opção 1: Verificar Task Scheduler
# Resultado esperado: Task deve existir novamente
```

#### Cenário 3: Matar Agent.exe
```powershell
.\debug-scenarios.ps1
# Escolha opção 7: Matar processo Agent.exe
# Aguarde ~10 segundos
# Escolha opção 6: Verificar processo Agent.exe
# Resultado esperado: Agent.exe deve estar rodando novamente
```

## Logs

O serviço escreve logs em dois lugares:

### 1. Event Viewer (Windows Event Log)

**Via PowerShell:**
```powershell
# Ver últimos 20 logs
Get-EventLog -LogName Application -Source PaneasMonitorService -Newest 20

# Ver logs em tempo real
Get-EventLog -LogName Application -Source PaneasMonitorService -Newest 20 | Format-Table -AutoSize

# Filtrar por nível
Get-EventLog -LogName Application -Source PaneasMonitorService -EntryType Error -Newest 10
```

**Via Event Viewer GUI:**
1. Abra Event Viewer (Win+R → `eventvwr`)
2. Navegue: Windows Logs → Application
3. Filtrar por Source: `PaneasMonitorService`

### 2. Console Output (modo debug)

Quando executa via `test-console.ps1`, os logs aparecem diretamente no console.

## Níveis de Log

- **Information**: Eventos normais (serviço iniciou, task criada, etc.)
- **Debug**: Detalhes de monitoramento (sessão ativa, Agent.exe rodando, etc.)
- **Warning**: Problemas detectados (task desabilitada, Agent.exe não rodando)
- **Error**: Erros de execução

## Troubleshooting

### Serviço não inicia

**Sintoma:** `Status: Stopped` após `Start-Service`

**Verificar:**
1. Logs do Event Viewer
2. Se caminho do Agent.exe está correto em `appsettings.json`
3. Permissões do executável
4. Se .NET 8.0 Runtime está instalado

**Solução:**
```powershell
# Ver erro específico
Get-EventLog -LogName Application -Source PaneasMonitorService -EntryType Error -Newest 1 | Format-List
```

### Task Scheduler não é criada

**Sintoma:** Logs mostram erro ao criar task

**Verificar:**
1. Se serviço está rodando como SYSTEM
2. Permissões no Task Scheduler
3. Se TaskScheduler library está instalada

**Solução:**
```powershell
# Verificar identity do serviço
sc.exe qc PaneasMonitorService
# Deve mostrar: SERVICE_START_NAME : LocalSystem
```

### Agent.exe não inicia

**Sintoma:** Task existe mas Agent.exe não roda

**Verificar:**
1. Caminho do Agent.exe em `appsettings.json`
2. Se Agent.exe existe no caminho especificado
3. Se há sessão de usuário ativa (não apenas Session 0)
4. Permissões de execução do Agent.exe

**Debug:**
```powershell
# Executar task manualmente
.\debug-scenarios.ps1
# Opção 5: Executar Task Scheduler manualmente
# Opção 6: Verificar processo Agent.exe

# Ver erro da task
Get-ScheduledTask -TaskName PaneasMonitorTask | Get-ScheduledTaskInfo
```

### Serviço consome muita CPU/RAM

**Sintoma:** Uso alto de recursos

**Verificar:**
1. Intervalo de monitoramento em `appsettings.json`
2. Se há loop infinito nos logs

**Solução:**
```powershell
# Aumentar intervalo
# Edite appsettings.json:
# "MonitorIntervalSeconds": 30  (ao invés de 10)

# Reinicie o serviço
Restart-Service PaneasMonitorService
```

## Proteção Contra Desinstalação

Este serviço oferece proteção básica:

- ✅ Protege contra usuários padrão (matando processo, desabilitando task)
- ✅ Auto-recovery em ~10 segundos
- ⚠️ Administradores ainda podem desinstalar o serviço

Para proteção adicional contra administradores, veja:
- `docs/deployment-service-task-hybrid.md` - Seção "Phase 3: Hardening"
- Implementar proteção de registro
- Configurar Group Policy restrictions
- Usar DACL para proteger arquivos

## Estrutura do Projeto

```
PaneasMonitorService/
├── Program.cs              # Entry point, configuração do serviço
├── MonitorService.cs       # Lógica principal de monitoramento
├── appsettings.json        # Configuração
├── PaneasMonitorService.csproj
├── Scripts/
│   ├── install-service.ps1      # Instalação
│   ├── uninstall-service.ps1    # Desinstalação
│   ├── test-console.ps1         # Teste em console
│   └── debug-scenarios.ps1      # Debug interativo
└── README.md
```

## APIs Windows Utilizadas

### WTS (Windows Terminal Services)
- `WTSGetActiveConsoleSessionId()` - Detecta sessão ativa do usuário

### Process Management
- `ProcessIdToSessionId()` - Identifica Session ID de um processo
- `Process.GetProcessesByName()` - Lista processos Agent.exe

### Task Scheduler
- `TaskService` - Gerenciamento de tasks via COM API
- `Microsoft.Win32.TaskScheduler` library (TaskScheduler NuGet package)

## Dependências

- **.NET 8.0** (ou superior)
- **Microsoft.Extensions.Hosting.WindowsServices** 9.0.0
- **TaskScheduler** 2.11.1+ (biblioteca de Task Scheduler COM wrapper)

## Próximos Passos

Após validar o funcionamento:

1. **Criar instalador MSI** (WiX Toolset)
   - Ver: `docs/deployment-installer-wix-gpo.md`

2. **Distribuir via Group Policy**
   - Configurar GPO para instalação automática
   - Distribuir certificado auto-assinado

3. **Hardening adicional** (Fase 3)
   - Proteção de registro
   - File system DACLs
   - Group Policy restrictions

## Licença

POC / Projeto Educacional
