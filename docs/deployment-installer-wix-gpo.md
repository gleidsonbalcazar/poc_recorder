# Windows Installer (MSI) + GPO Deployment Guide
## Paneas Monitor - Enterprise Internal Deployment (100% FREE)

**Data:** 2025-11-07
**Versão:** 1.0
**Custo Total:** $0 (Solução Gratuita)
**Ambiente:** Active Directory Domain
**Certificado:** Self-Signed (Internal Use)

---

## Visão Geral

Este guia fornece instruções completas para criar um instalador MSI profissional usando **WiX Toolset** (gratuito) e distribuir em massa via **Group Policy** em ambiente Active Directory, usando **self-signed certificate** para evitar avisos do Windows SmartScreen.

### Solução Ideal Para:
- ✅ Deployments internos em empresas
- ✅ Ambientes Active Directory
- ✅ Máquinas domain-joined
- ✅ Budget zero (100% gratuito)
- ✅ Controle total sobre instalação

### Não Recomendado Para:
- ❌ Distribuição externa (fora do domain)
- ❌ Máquinas não domain-joined
- ❌ Deployment público/consumidor

---

## O Que Será Criado

```
┌─────────────────────────────────────────────────────────┐
│                 PaneasMonitor.msi                        │
│                                                          │
│  Instalador MSI profissional que:                       │
│  • Copia arquivos para Program Files                    │
│  • Instala Windows Service (PaneasMonitorService)       │
│  • Cria Task Scheduler task                             │
│  • Configura recovery options                           │
│  • Cria uninstaller                                     │
│  • Assinado com self-signed certificate                 │
│                                                          │
└─────────────────────────────────────────────────────────┘
          │
          ↓ Distribuído via GPO
          ↓
┌─────────────────────────────────────────────────────────┐
│          Domain Computers (Automático)                   │
│                                                          │
│  • Instalação silenciosa no boot/login                  │
│  • Sem avisos do SmartScreen                            │
│  • Sem interação do usuário                             │
│  • Rollout centralizado e controlado                    │
│                                                          │
└─────────────────────────────────────────────────────────┘
```

---

## Pré-Requisitos

### Software Necessário:

1. **Windows 10/11 ou Windows Server 2016+**
   - Com privilégios de administrador

2. **Visual Studio 2022** (Community Edition - Gratuita)
   - Download: https://visualstudio.microsoft.com/
   - Workload necessário: ".NET desktop development"

3. **WiX Toolset 4** (Gratuito)
   - Download: https://wixtoolset.org/
   - Extensão Visual Studio: "HeatWave for VS2022"

4. **Windows SDK** (Gratuito)
   - Incluído no Visual Studio 2022
   - Necessário para `signtool.exe`

5. **Git** (Para version control)
   - Já instalado no projeto

### Infraestrutura Necessária:

1. **Active Directory Domain**
   - Windows Server com AD DS
   - Group Policy Management Console

2. **File Share para Distribuição**
   - Exemplo: `\\SERVER\Software\PaneasMonitor\`
   - Permissões: Domain Computers (Read)

3. **Máquinas de Teste**
   - VMs ou computadores físicos
   - Domain-joined
   - Windows 10/11

---

## Parte 1: Instalação do WiX Toolset

### Passo 1: Instalar Visual Studio 2022

```powershell
# Download Visual Studio 2022 Community
# https://visualstudio.microsoft.com/downloads/

# Durante instalação, selecionar:
# - .NET desktop development
# - Windows SDK (10.0.19041.0 ou superior)
```

### Passo 2: Instalar WiX Toolset 4

```powershell
# Método 1: Via Visual Studio Extension Manager
# 1. Abrir Visual Studio 2022
# 2. Extensions → Manage Extensions
# 3. Buscar "HeatWave"
# 4. Instalar "HeatWave for VS2022"
# 5. Reiniciar Visual Studio

# Método 2: Download direto
# https://wixtoolset.org/downloads/
# Instalar WiX Toolset v4.x
```

### Passo 3: Verificar Instalação

```powershell
# Verificar se WiX está instalado
wix --version

# Verificar signtool (Windows SDK)
signtool /?

# Se signtool não encontrado, adicionar ao PATH:
# C:\Program Files (x86)\Windows Kits\10\bin\10.0.19041.0\x64\
```

---

## Parte 2: Criar Projeto WiX

### Estrutura do Projeto

```
Pan/
├── c2-agent/
│   ├── Agent/                      # Projeto existente
│   ├── PaneasMonitorService/       # Service (se implementado)
│   └── PaneasMonitor.Installer/    # ← NOVO: Projeto WiX
│       ├── PaneasMonitor.Installer.wixproj
│       ├── Product.wxs             # Definição principal
│       ├── ServiceInstall.wxs      # Windows Service
│       ├── TaskScheduler.wxs       # Task Scheduler
│       ├── Files.wxs               # Componentes de arquivos
│       └── Resources/
│           ├── task-definition.xml
│           └── icon.ico (opcional)
```

### Passo 1: Criar Projeto WiX no Visual Studio

```
1. Abrir Visual Studio 2022
2. File → New → Project
3. Buscar "WiX"
4. Selecionar "MSI Package (WiX v4)"
5. Nome: PaneasMonitor.Installer
6. Location: c2-agent/
7. Create
```

### Passo 2: Adicionar ao Solution Existente

```
1. Solution Explorer → Right-click solution
2. Add → Existing Project
3. Selecionar PaneasMonitor.Installer.wixproj
4. Adicionar referência do Agent project:
   - Right-click PaneasMonitor.Installer → Add → Reference
   - Selecionar Agent project
```

---

## Parte 3: Código WiX - Product.wxs

Este é o arquivo principal do instalador.

```xml
<?xml version="1.0" encoding="UTF-8"?>
<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs"
     xmlns:util="http://wixtoolset.org/schemas/v4/wxs/util">

  <Package Name="Paneas Monitor"
           Manufacturer="Your Company Name"
           Version="1.0.0.0"
           UpgradeCode="12345678-1234-1234-1234-123456789ABC"
           Language="1033"
           Codepage="1252"
           InstallerVersion="500"
           Compressed="yes"
           InstallScope="perMachine">

    <!-- Upgrade Logic: Permite atualizar versão instalada -->
    <MajorUpgrade
      DowngradeErrorMessage="Uma versão mais recente já está instalada."
      Schedule="afterInstallInitialize"
      AllowSameVersionUpgrades="yes" />

    <!-- Compactar todos os arquivos no MSI -->
    <MediaTemplate EmbedCab="yes" CompressionLevel="high" />

    <!-- Ícone no Add/Remove Programs -->
    <Icon Id="ProductIcon" SourceFile="Resources\icon.ico"/>
    <Property Id="ARPPRODUCTICON" Value="ProductIcon" />

    <!-- URLs no Add/Remove Programs -->
    <Property Id="ARPHELPLINK" Value="https://yourcompany.com/support" />
    <Property Id="ARPURLINFOABOUT" Value="https://yourcompany.com" />

    <!-- Verificar pré-requisitos -->
    <PropertyRef Id="WIX_IS_NETFRAMEWORK_48_OR_LATER_INSTALLED"/>
    <Launch Condition="Installed OR WIX_IS_NETFRAMEWORK_48_OR_LATER_INSTALLED"
            Message="Esta aplicação requer .NET 8.0 Runtime ou superior. Por favor, instale o .NET 8.0 antes de continuar."/>

    <!-- Verificar privilégios de admin -->
    <Property Id="ALLUSERS" Value="1" />

    <!-- Propriedades de Configuração (podem ser passadas via command line) -->
    <Property Id="C2_SERVER_URL" Value="http://localhost:8000" />
    <Property Id="OPERATION_MODE" Value="hybrid" />

    <!-- =============================================== -->
    <!-- ESTRUTURA DE DIRETÓRIOS                        -->
    <!-- =============================================== -->

    <StandardDirectory Id="ProgramFiles64Folder">
      <Directory Id="INSTALLFOLDER" Name="Paneas Monitor">
        <Directory Id="BinFolder" Name="bin" />
        <Directory Id="FFmpegFolder" Name="ffmpeg" />
        <Directory Id="ConfigFolder" Name="config" />
        <Directory Id="LogsFolder" Name="logs" />
        <Directory Id="DataFolder" Name="data" />
      </Directory>
    </StandardDirectory>

    <!-- ProgramData (para database e logs persistentes) -->
    <StandardDirectory Id="CommonAppDataFolder">
      <Directory Id="ProgramDataFolder" Name="Paneas Monitor" />
    </StandardDirectory>

    <!-- Start Menu -->
    <StandardDirectory Id="ProgramMenuFolder">
      <Directory Id="ApplicationProgramsFolder" Name="Paneas Monitor"/>
    </StandardDirectory>

    <!-- =============================================== -->
    <!-- FEATURES (O que será instalado)               -->
    <!-- =============================================== -->

    <Feature Id="Complete"
             Title="Paneas Monitor"
             Description="Sistema completo de monitoramento e gravação."
             Level="1"
             ConfigurableDirectory="INSTALLFOLDER"
             AllowAbsent="no"
             Display="expand">

      <!-- Aplicação Principal -->
      <Feature Id="MainApplication"
               Title="Aplicação Principal"
               Description="Agent.exe e dependências principais."
               Level="1">
        <ComponentGroupRef Id="ProductComponents" />
      </Feature>

      <!-- Windows Service (se implementado) -->
      <Feature Id="WindowsService"
               Title="Windows Service"
               Description="Serviço Windows para monitoramento contínuo."
               Level="1">
        <ComponentGroupRef Id="ServiceComponents" />
      </Feature>

      <!-- Task Scheduler -->
      <Feature Id="TaskScheduler"
               Title="Agendamento Automático"
               Description="Task Scheduler para inicialização automática."
               Level="1">
        <ComponentGroupRef Id="TaskSchedulerComponents" />
      </Feature>

      <!-- Shortcuts (Opcional) -->
      <Feature Id="Shortcuts"
               Title="Atalhos"
               Description="Atalhos no Menu Iniciar."
               Level="1">
        <ComponentGroupRef Id="ShortcutComponents" />
      </Feature>
    </Feature>

    <!-- =============================================== -->
    <!-- UI CONFIGURATION                               -->
    <!-- =============================================== -->

    <UI>
      <!-- Usar UI padrão do WiX (Minimal para instalação silenciosa) -->
      <UIRef Id="WixUI_Minimal" />

      <!-- Texto de licença (opcional) -->
      <Publish Dialog="WelcomeDlg"
               Control="Next"
               Event="NewDialog"
               Value="InstallDirDlg">1</Publish>
    </UI>

  </Package>
</Wix>
```

**IMPORTANTE:** Gerar GUID único para `UpgradeCode`:
```powershell
# No PowerShell:
[guid]::NewGuid().ToString().ToUpper()
# Copiar resultado e colar no UpgradeCode
```

---

## Parte 4: Files.wxs - Componentes de Arquivos

Este arquivo define quais arquivos serão instalados.

```xml
<?xml version="1.0" encoding="UTF-8"?>
<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
  <Fragment>

    <ComponentGroup Id="ProductComponents">

      <!-- Agent.exe - Executável Principal -->
      <Component Id="AgentExe"
                 Directory="BinFolder"
                 Guid="11111111-1111-1111-1111-111111111111">
        <File Id="Agent.exe"
              Source="$(var.Agent.TargetPath)"
              KeyPath="yes"
              Checksum="yes"/>
      </Component>

      <!-- Dependências DLL (se não usar self-contained) -->
      <!-- Se usar self-contained, Agent.exe já contém tudo -->

      <!-- FFmpeg Executables -->
      <Component Id="FFmpegExe"
                 Directory="FFmpegFolder"
                 Guid="22222222-2222-2222-2222-222222222222">
        <File Id="ffmpeg.exe"
              Source="$(var.Agent.ProjectDir)ffmpeg\ffmpeg.exe"
              KeyPath="yes"/>
        <File Id="ffprobe.exe"
              Source="$(var.Agent.ProjectDir)ffmpeg\ffprobe.exe"/>
      </Component>

      <!-- Configuration Files -->
      <Component Id="ConfigFiles"
                 Directory="ConfigFolder"
                 Guid="33333333-3333-3333-3333-333333333333">
        <File Id="appsettings.json"
              Source="$(var.Agent.ProjectDir)appsettings.json"
              KeyPath="yes"/>
      </Component>

      <!-- Empty Directories (Logs, Data) -->
      <Component Id="LogsDirectory"
                 Directory="LogsFolder"
                 Guid="44444444-4444-4444-4444-444444444444">
        <CreateFolder/>
        <RemoveFolder Id="RemoveLogs" On="uninstall"/>
        <RegistryValue Root="HKCU"
                      Key="Software\PaneasMonitor"
                      Type="string"
                      Value=""
                      KeyPath="yes"/>
      </Component>

      <Component Id="DataDirectory"
                 Directory="DataFolder"
                 Guid="55555555-5555-5555-5555-555555555555">
        <CreateFolder/>
        <RemoveFolder Id="RemoveData" On="uninstall"/>
        <RegistryValue Root="HKCU"
                      Key="Software\PaneasMonitor"
                      Type="string"
                      Value=""
                      KeyPath="yes"/>
      </Component>

      <!-- Registry Configuration -->
      <Component Id="RegistryConfig"
                 Directory="INSTALLFOLDER"
                 Guid="66666666-6666-6666-6666-666666666666">
        <RegistryKey Root="HKLM" Key="Software\PaneasMonitor\Config">
          <RegistryValue Type="string"
                        Name="ServerUrl"
                        Value="[C2_SERVER_URL]"
                        KeyPath="yes"/>
          <RegistryValue Type="string"
                        Name="OperationMode"
                        Value="[OPERATION_MODE]"/>
          <RegistryValue Type="string"
                        Name="InstallPath"
                        Value="[INSTALLFOLDER]"/>
        </RegistryKey>
      </Component>

    </ComponentGroup>

    <!-- =============================================== -->
    <!-- SHORTCUTS COMPONENTS                           -->
    <!-- =============================================== -->

    <ComponentGroup Id="ShortcutComponents">
      <Component Id="StartMenuShortcut"
                 Directory="ApplicationProgramsFolder"
                 Guid="77777777-7777-7777-7777-777777777777">
        <Shortcut Id="AgentStartMenuShortcut"
                  Name="Paneas Monitor"
                  Description="Paneas Monitor Agent"
                  Target="[BinFolder]Agent.exe"
                  WorkingDirectory="BinFolder"
                  Icon="ProductIcon"/>
        <RemoveFolder Id="ApplicationProgramsFolder" On="uninstall"/>
        <RegistryValue Root="HKCU"
                      Key="Software\PaneasMonitor"
                      Name="installed"
                      Type="integer"
                      Value="1"
                      KeyPath="yes"/>
      </Component>
    </ComponentGroup>

  </Fragment>
</Wix>
```

**IMPORTANTE:** Gerar GUIDs únicos para cada Component:
```powershell
# Gerar 10 GUIDs de uma vez:
1..10 | ForEach-Object { [guid]::NewGuid().ToString().ToUpper() }
```

---

## Parte 5: ServiceInstall.wxs - Windows Service

Se você implementou a arquitetura Service + Task Scheduler Hybrid:

```xml
<?xml version="1.0" encoding="UTF-8"?>
<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs"
     xmlns:util="http://wixtoolset.org/schemas/v4/wxs/util">
  <Fragment>

    <ComponentGroup Id="ServiceComponents">

      <Component Id="PaneasMonitorService"
                 Directory="BinFolder"
                 Guid="88888888-8888-8888-8888-888888888888">

        <!-- Service Executable -->
        <File Id="PaneasMonitorService.exe"
              Source="$(var.PaneasMonitorService.TargetPath)"
              KeyPath="yes"/>

        <!-- Service Installation -->
        <ServiceInstall Id="PaneasMonitorSvc"
                       Name="PaneasMonitor"
                       DisplayName="Paneas Monitor Service"
                       Description="Paneas Monitor - Sistema de monitoramento e gravação de tela"
                       Type="ownProcess"
                       Start="auto"
                       Account="LocalSystem"
                       ErrorControl="normal"
                       Interactive="no"
                       Vital="yes">
          <!-- Delayed Start (inicia após boot) -->
          <ServiceConfig DelayedAutoStart="yes"
                        OnInstall="yes"
                        OnReinstall="yes"/>
        </ServiceInstall>

        <!-- Service Control: Start/Stop -->
        <ServiceControl Id="StartService"
                       Name="PaneasMonitor"
                       Start="install"
                       Stop="both"
                       Remove="uninstall"
                       Wait="yes"/>

        <!-- Service Recovery: Restart on Failure -->
        <util:ServiceConfig ServiceName="PaneasMonitor"
                           FirstFailureActionType="restart"
                           SecondFailureActionType="restart"
                           ThirdFailureActionType="restart"
                           RestartServiceDelayInSeconds="60"
                           ResetPeriodInDays="1"/>
      </Component>

    </ComponentGroup>

  </Fragment>
</Wix>
```

---

## Parte 6: TaskScheduler.wxs - Task Scheduler

```xml
<?xml version="1.0" encoding="UTF-8"?>
<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
  <Fragment>

    <ComponentGroup Id="TaskSchedulerComponents">

      <!-- Task Definition XML File -->
      <Component Id="TaskDefinitionXML"
                 Directory="ConfigFolder"
                 Guid="99999999-9999-9999-9999-999999999999">
        <File Id="task_definition.xml"
              Source="Resources\task-definition.xml"
              KeyPath="yes"/>
      </Component>

    </ComponentGroup>

    <!-- Custom Action: Create Scheduled Task -->
    <CustomAction Id="CreateScheduledTask"
                  Return="check"
                  Impersonate="no"
                  Execute="deferred"
                  Directory="INSTALLFOLDER"
                  ExeCommand='[SystemFolder]SCHTASKS.EXE /CREATE /XML "[ConfigFolder]task-definition.xml" /TN "PaneasMonitorTask" /F'/>

    <!-- Custom Action: Delete Scheduled Task -->
    <CustomAction Id="DeleteScheduledTask"
                  Return="ignore"
                  Impersonate="no"
                  Execute="deferred"
                  Directory="INSTALLFOLDER"
                  ExeCommand='[SystemFolder]SCHTASKS.EXE /DELETE /TN "PaneasMonitorTask" /F'/>

    <!-- Install Sequence -->
    <InstallExecuteSequence>
      <Custom Action="CreateScheduledTask" After="InstallFiles">
        NOT Installed
      </Custom>
      <Custom Action="DeleteScheduledTask" Before="RemoveFiles">
        Installed AND (REMOVE="ALL")
      </Custom>
    </InstallExecuteSequence>

  </Fragment>
</Wix>
```

### Resources/task-definition.xml

```xml
<?xml version="1.0" encoding="UTF-16"?>
<Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
  <RegistrationInfo>
    <Description>Paneas Monitor - Gravação Automática</Description>
    <URI>\PaneasMonitorTask</URI>
  </RegistrationInfo>
  <Triggers>
    <BootTrigger>
      <Enabled>true</Enabled>
      <Delay>PT1M</Delay>
    </BootTrigger>
  </Triggers>
  <Principals>
    <Principal id="Author">
      <UserId>S-1-5-18</UserId> <!-- SYSTEM -->
      <RunLevel>HighestAvailable</RunLevel>
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
    <Hidden>false</Hidden>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
  </Settings>
  <Actions Context="Author">
    <Exec>
      <Command>"C:\Program Files\Paneas Monitor\bin\Agent.exe"</Command>
      <WorkingDirectory>C:\Program Files\Paneas Monitor\bin</WorkingDirectory>
    </Exec>
  </Actions>
</Task>
```

---

## Parte 7: Build do Instalador

### Configuração do Projeto

**PaneasMonitor.Installer.wixproj:**

```xml
<Project Sdk="WixToolset.Sdk/4.0.0">
  <PropertyGroup>
    <OutputName>PaneasMonitor</OutputName>
    <OutputType>Package</OutputType>
    <Platform>x64</Platform>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="WixToolset.UI.wixext" Version="4.0.0" />
    <PackageReference Include="WixToolset.Util.wixext" Version="4.0.0" />
  </ItemGroup>

  <ItemGroup>
    <!-- Referência ao projeto Agent -->
    <ProjectReference Include="..\Agent\Agent.csproj">
      <Name>Agent</Name>
      <Project>{AGENT-PROJECT-GUID}</Project>
    </ProjectReference>
  </ItemGroup>

  <ItemGroup>
    <Compile Include="Product.wxs" />
    <Compile Include="Files.wxs" />
    <Compile Include="ServiceInstall.wxs" />
    <Compile Include="TaskScheduler.wxs" />
  </ItemGroup>

  <ItemGroup>
    <Content Include="Resources\**" />
  </ItemGroup>
</Project>
```

### Build via Visual Studio

```
1. Abrir Solution no Visual Studio
2. Selecionar configuração: Release | x64
3. Build → Build Solution (F6)
4. Resultado: PaneasMonitor.Installer\bin\Release\PaneasMonitor.msi
```

### Build via Command Line

```powershell
# Navegar para diretório do installer
cd c2-agent\PaneasMonitor.Installer

# Build Release
dotnet build -c Release -p:Platform=x64

# Resultado em: bin\Release\net8.0-windows\x64\PaneasMonitor.msi
```

---

## Parte 8: Self-Signed Certificate (GRATUITO)

### Passo 1: Criar Self-Signed Certificate

```powershell
# Executar PowerShell como Administrador

# 1. Criar certificado de code signing
$cert = New-SelfSignedCertificate `
    -Type CodeSigningCert `
    -Subject "CN=Paneas Monitor, O=Your Company, C=US" `
    -FriendlyName "Paneas Monitor Code Signing" `
    -CertStoreLocation Cert:\CurrentUser\My `
    -NotAfter (Get-Date).AddYears(5)

# 2. Ver certificado criado
Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -like "*Paneas Monitor*" }

# 3. Copiar Thumbprint (você vai usar depois)
$thumbprint = $cert.Thumbprint
Write-Host "Thumbprint: $thumbprint" -ForegroundColor Green

# 4. Exportar certificado para distribuição via GPO
$certPath = "C:\Temp\PaneasMonitor-CodeSigning.cer"
Export-Certificate -Cert $cert -FilePath $certPath

Write-Host "Certificado exportado para: $certPath" -ForegroundColor Green
Write-Host "Use este arquivo para distribuir via GPO!" -ForegroundColor Yellow
```

### Passo 2: Assinar Executáveis e MSI

```powershell
# Script: sign-files.ps1

param(
    [string]$Thumbprint = "COLE_SEU_THUMBPRINT_AQUI",
    [string]$TimestampServer = "http://timestamp.digicert.com"
)

# Caminhos
$agentExe = ".\bin\Release\net8.0\win-x64\publish\Agent.exe"
$msi = ".\PaneasMonitor.Installer\bin\Release\net8.0-windows\x64\PaneasMonitor.msi"

# Assinar Agent.exe
Write-Host "Assinando Agent.exe..." -ForegroundColor Cyan
signtool sign /sha1 $Thumbprint /fd SHA256 /td SHA256 /tr $TimestampServer $agentExe

if ($LASTEXITCODE -eq 0) {
    Write-Host "✓ Agent.exe assinado com sucesso!" -ForegroundColor Green
} else {
    Write-Host "✗ Erro ao assinar Agent.exe" -ForegroundColor Red
    exit 1
}

# Assinar MSI
Write-Host "Assinando PaneasMonitor.msi..." -ForegroundColor Cyan
signtool sign /sha1 $Thumbprint /fd SHA256 /td SHA256 /tr $TimestampServer $msi

if ($LASTEXITCODE -eq 0) {
    Write-Host "✓ MSI assinado com sucesso!" -ForegroundColor Green
} else {
    Write-Host "✗ Erro ao assinar MSI" -ForegroundColor Red
    exit 1
}

# Verificar assinaturas
Write-Host "`nVerificando assinaturas..." -ForegroundColor Cyan
signtool verify /pa /v $agentExe
signtool verify /pa /v $msi

Write-Host "`n✓ Processo de assinatura concluído!" -ForegroundColor Green
```

**Uso:**
```powershell
# 1. Editar script e colar seu Thumbprint
# 2. Executar:
.\sign-files.ps1

# Ou passar thumbprint como parâmetro:
.\sign-files.ps1 -Thumbprint "1234567890ABCDEF1234567890ABCDEF12345678"
```

### Passo 3: Verificar Assinatura

```powershell
# Verificar se arquivo está assinado
signtool verify /pa /v PaneasMonitor.msi

# Resultado esperado:
# Successfully verified: PaneasMonitor.msi
# Signing Certificate Chain:
#     Issued to: Paneas Monitor
#     Issued by: Paneas Monitor
```

---

## Parte 9: Distribuir Certificado via GPO

Para evitar avisos do SmartScreen, você precisa distribuir o certificado para todas as máquinas do domain.

### Método 1: Via Group Policy Management Console (GUI)

```
1. Abrir Server Manager no Domain Controller
2. Tools → Group Policy Management
3. Navegar até: Forest → Domains → YourDomain.com
4. Right-click Default Domain Policy → Edit

5. Navegar até:
   Computer Configuration
   → Policies
   → Windows Settings
   → Security Settings
   → Public Key Policies
   → Trusted Publishers

6. Right-click Trusted Publishers → Import
7. Selecionar: C:\Temp\PaneasMonitor-CodeSigning.cer
8. Next → Next → Finish

9. Repetir para "Trusted Root Certification Authorities"
   (mesmo caminho, mas importar em Trusted Root)

10. Fechar Group Policy Editor
11. Force update nos clients:
    gpupdate /force
```

### Método 2: Via PowerShell (Automático)

```powershell
# Script: deploy-certificate-gpo.ps1
# Executar no Domain Controller como Domain Admin

param(
    [string]$CertPath = "\\SERVER\Share\PaneasMonitor-CodeSigning.cer",
    [string]$GPOName = "Paneas Monitor Certificate Distribution"
)

# Importar módulo GroupPolicy
Import-Module GroupPolicy

# Criar novo GPO (ou usar existente)
try {
    $gpo = Get-GPO -Name $GPOName -ErrorAction Stop
    Write-Host "GPO existente encontrado: $GPOName" -ForegroundColor Yellow
} catch {
    $gpo = New-GPO -Name $GPOName
    Write-Host "Novo GPO criado: $GPOName" -ForegroundColor Green
}

# Link GPO ao Domain (aplica a todos os computers)
$domain = (Get-ADDomain).DistinguishedName
New-GPLink -Name $GPOName -Target $domain -LinkEnabled Yes -ErrorAction SilentlyContinue

# Importar certificado para Trusted Publishers
Write-Host "Configurando certificado no GPO..." -ForegroundColor Cyan

# Caminho do certificado no GPO
$gpoPath = "\\$env:USERDNSDOMAIN\SYSVOL\$env:USERDNSDOMAIN\Policies\{$($gpo.Id)}"
$certDestPath = "$gpoPath\Machine\Microsoft\Windows NT\SecEdit"

# Criar diretório se não existir
New-Item -ItemType Directory -Path $certDestPath -Force | Out-Null

# Copiar certificado
Copy-Item -Path $CertPath -Destination "$certDestPath\PaneasMonitor.cer" -Force

Write-Host "✓ Certificado distribuído via GPO!" -ForegroundColor Green
Write-Host "GPO Name: $GPOName" -ForegroundColor Cyan
Write-Host "Linked to: $domain" -ForegroundColor Cyan
Write-Host "`nExecute 'gpupdate /force' nos clients para aplicar." -ForegroundColor Yellow
```

### Método 3: Script PowerShell Direto (Sem GPO)

Se você não quer usar GPO, pode executar este script em cada máquina:

```powershell
# Script: install-certificate.ps1
# Executar em cada máquina como Administrador

param(
    [string]$CertPath = "\\SERVER\Share\PaneasMonitor-CodeSigning.cer"
)

# Importar certificado para Trusted Publishers (máquina local)
$cert = Import-Certificate -FilePath $CertPath -CertStoreLocation Cert:\LocalMachine\TrustedPublisher

Write-Host "✓ Certificado importado: $($cert.Subject)" -ForegroundColor Green

# Importar também para Root (necessário para self-signed)
Import-Certificate -FilePath $CertPath -CertStoreLocation Cert:\LocalMachine\Root

Write-Host "✓ Certificado adicionado às Trusted Root Authorities" -ForegroundColor Green
Write-Host "`nSmartScreen não mostrará mais avisos para arquivos assinados com este certificado." -ForegroundColor Cyan
```

---

## Parte 10: Distribuir MSI via GPO

Agora vamos configurar o deployment automático do instalador.

### Passo 1: Preparar Distribution Point

```powershell
# No servidor de arquivos:

# 1. Criar diretório compartilhado
New-Item -ItemType Directory -Path "C:\Software\PaneasMonitor" -Force
New-SmbShare -Name "Software" -Path "C:\Software" -FullAccess "Domain Admins" -ReadAccess "Domain Computers"

# 2. Configurar permissões NTFS
$acl = Get-Acl "C:\Software\PaneasMonitor"
$accessRule = New-Object System.Security.AccessControl.FileSystemAccessRule("Domain Computers","Read","Allow")
$acl.SetAccessRule($accessRule)
Set-Acl "C:\Software\PaneasMonitor" $acl

# 3. Copiar MSI para o share
Copy-Item ".\PaneasMonitor.Installer\bin\Release\PaneasMonitor.msi" "C:\Software\PaneasMonitor\"

Write-Host "✓ MSI copiado para: \\$env:COMPUTERNAME\Software\PaneasMonitor\" -ForegroundColor Green
```

### Passo 2: Criar GPO para Software Installation

```
1. Group Policy Management Console
2. Right-click Domain ou OU → Create a GPO
3. Nome: "Deploy Paneas Monitor"
4. Right-click novo GPO → Edit

5. Navegar até:
   Computer Configuration
   → Policies
   → Software Settings
   → Software Installation

6. Right-click Software Installation → New → Package

7. Selecionar MSI:
   \\SERVER\Software\PaneasMonitor\PaneasMonitor.msi
   ⚠️ IMPORTANTE: Usar UNC path, NÃO caminho local

8. Deployment Method: Assigned (não Published)

9. OK

10. Right-click no pacote → Properties
    - Deployment: Advanced
    - Installation UI: Basic
    - Uninstall when out of scope: Yes (opcional)
    - Make available to x64 machines: Yes
```

### Passo 3: Configurar Deployment Options

```
Properties do pacote instalado:

Tab: Deployment
  ☑ Assigned
  ☐ Published
  ☑ Install this application at logon
  ☑ Uninstall this application when it falls out of scope
  ☐ Do not display in Add/Remove Programs

Tab: Upgrades
  ☑ Upgrade existing installations (for future versions)

Tab: Advanced
  ☑ Ignore language when deploying
  ☑ Make 32-bit X86 application available to Win64 machines
```

### Passo 4: Link GPO e Force Update

```powershell
# Link GPO ao Domain (se ainda não linkado)
$gpoName = "Deploy Paneas Monitor"
$domain = (Get-ADDomain).DistinguishedName
New-GPLink -Name $gpoName -Target $domain -LinkEnabled Yes

# Force update em computadores específicos (teste)
$testComputers = @("PC001", "PC002", "PC003")

foreach ($computer in $testComputers) {
    Write-Host "Forcing GPUpdate on $computer..." -ForegroundColor Cyan
    Invoke-Command -ComputerName $computer -ScriptBlock {
        gpupdate /force
    }
}

Write-Host "✓ GPO deployment configurado!" -ForegroundColor Green
Write-Host "Computadores instalarão automaticamente no próximo boot/login." -ForegroundColor Yellow
```

---

## Parte 11: Scripts PowerShell Auxiliares

### Script: build-and-deploy.ps1 (Completo)

```powershell
# build-and-deploy.ps1
# Script completo de build, assinatura e deployment

param(
    [string]$Configuration = "Release",
    [string]$CertThumbprint = "COLE_SEU_THUMBPRINT",
    [string]$DistributionPath = "\\SERVER\Software\PaneasMonitor",
    [switch]$SkipBuild,
    [switch]$SkipSign,
    [switch]$Deploy
)

$ErrorActionPreference = "Stop"

Write-Host "=== Paneas Monitor - Build & Deploy Script ===" -ForegroundColor Cyan
Write-Host ""

# Passo 1: Build Agent (Self-Contained)
if (-not $SkipBuild) {
    Write-Host "[1/5] Building Agent.exe..." -ForegroundColor Yellow

    cd c2-agent\Agent
    dotnet clean
    dotnet publish -c $Configuration -r win-x64 --self-contained -p:PublishSingleFile=false -p:PublishTrimmed=false

    if ($LASTEXITCODE -ne 0) {
        Write-Host "✗ Build failed!" -ForegroundColor Red
        exit 1
    }

    Write-Host "✓ Agent.exe built successfully!" -ForegroundColor Green
    cd ..\..
}

# Passo 2: Build MSI
if (-not $SkipBuild) {
    Write-Host "[2/5] Building MSI installer..." -ForegroundColor Yellow

    cd c2-agent\PaneasMonitor.Installer
    dotnet build -c $Configuration -p:Platform=x64

    if ($LASTEXITCODE -ne 0) {
        Write-Host "✗ MSI build failed!" -ForegroundColor Red
        exit 1
    }

    Write-Host "✓ MSI built successfully!" -ForegroundColor Green
    cd ..\..
}

# Passo 3: Sign Files
if (-not $SkipSign) {
    Write-Host "[3/5] Signing files..." -ForegroundColor Yellow

    $agentExe = "c2-agent\Agent\bin\Release\net8.0\win-x64\publish\Agent.exe"
    $msi = "c2-agent\PaneasMonitor.Installer\bin\Release\net8.0-windows\x64\PaneasMonitor.msi"

    signtool sign /sha1 $CertThumbprint /fd SHA256 /td SHA256 /tr "http://timestamp.digicert.com" $agentExe
    signtool sign /sha1 $CertThumbprint /fd SHA256 /td SHA256 /tr "http://timestamp.digicert.com" $msi

    Write-Host "✓ Files signed successfully!" -ForegroundColor Green
}

# Passo 4: Copy to Distribution Point
Write-Host "[4/5] Copying MSI to distribution point..." -ForegroundColor Yellow

$msiSource = "c2-agent\PaneasMonitor.Installer\bin\Release\net8.0-windows\x64\PaneasMonitor.msi"
$msiDest = "$DistributionPath\PaneasMonitor.msi"

New-Item -ItemType Directory -Path $DistributionPath -Force | Out-Null
Copy-Item -Path $msiSource -Destination $msiDest -Force

Write-Host "✓ MSI copied to: $msiDest" -ForegroundColor Green

# Passo 5: Deploy via GPO (opcional)
if ($Deploy) {
    Write-Host "[5/5] Triggering GPO update on test computers..." -ForegroundColor Yellow

    $testComputers = @("PC001", "PC002")
    foreach ($computer in $testComputers) {
        Invoke-Command -ComputerName $computer -ScriptBlock {
            gpupdate /force
        } -ErrorAction Continue
    }

    Write-Host "✓ GPO update triggered!" -ForegroundColor Green
}

Write-Host ""
Write-Host "=== Build & Deploy Complete! ===" -ForegroundColor Green
Write-Host "MSI Location: $msiDest" -ForegroundColor Cyan
```

**Uso:**
```powershell
# Build completo + sign + deploy
.\build-and-deploy.ps1 -CertThumbprint "YOUR_THUMBPRINT" -Deploy

# Apenas copiar MSI (skip build/sign)
.\build-and-deploy.ps1 -SkipBuild -SkipSign
```

### Script: verify-deployment.ps1

```powershell
# verify-deployment.ps1
# Verificar status de deployment em múltiplas máquinas

param(
    [string[]]$ComputerNames = @()
)

if ($ComputerNames.Count -eq 0) {
    # Buscar todos os computadores do domain
    $ComputerNames = (Get-ADComputer -Filter * -SearchBase "OU=Workstations,DC=domain,DC=com").Name
}

$results = @()

foreach ($computer in $ComputerNames) {
    Write-Host "Checking $computer..." -ForegroundColor Cyan

    try {
        $installed = Invoke-Command -ComputerName $computer -ScriptBlock {
            $app = Get-WmiObject -Class Win32_Product | Where-Object { $_.Name -eq "Paneas Monitor" }
            $service = Get-Service -Name "PaneasMonitor" -ErrorAction SilentlyContinue
            $task = Get-ScheduledTask -TaskName "PaneasMonitorTask" -ErrorAction SilentlyContinue

            [PSCustomObject]@{
                Installed = ($app -ne $null)
                Version = $app.Version
                ServiceStatus = $service.Status
                TaskExists = ($task -ne $null)
                TaskEnabled = $task.State -eq "Ready"
            }
        } -ErrorAction Stop

        $results += [PSCustomObject]@{
            Computer = $computer
            Status = "Online"
            Installed = $installed.Installed
            Version = $installed.Version
            ServiceRunning = $installed.ServiceStatus -eq "Running"
            TaskConfigured = $installed.TaskExists -and $installed.TaskEnabled
        }

        $status = if ($installed.Installed) { "✓" } else { "✗" }
        Write-Host "  $status $computer - Installed: $($installed.Installed)" -ForegroundColor $(if ($installed.Installed) { "Green" } else { "Red" })

    } catch {
        $results += [PSCustomObject]@{
            Computer = $computer
            Status = "Offline/Error"
            Installed = $false
            Version = "N/A"
            ServiceRunning = $false
            TaskConfigured = $false
        }

        Write-Host "  ✗ $computer - Error: $($_.Exception.Message)" -ForegroundColor Red
    }
}

# Export report
$reportPath = "deployment-report-$(Get-Date -Format 'yyyyMMdd-HHmmss').csv"
$results | Export-Csv -Path $reportPath -NoTypeInformation

# Summary
$totalComputers = $results.Count
$installedCount = ($results | Where-Object { $_.Installed }).Count
$successRate = [math]::Round(($installedCount / $totalComputers) * 100, 2)

Write-Host ""
Write-Host "=== Deployment Summary ===" -ForegroundColor Cyan
Write-Host "Total Computers: $totalComputers" -ForegroundColor White
Write-Host "Installed: $installedCount" -ForegroundColor Green
Write-Host "Not Installed: $($totalComputers - $installedCount)" -ForegroundColor Red
Write-Host "Success Rate: $successRate%" -ForegroundColor $(if ($successRate -ge 90) { "Green" } else { "Yellow" })
Write-Host ""
Write-Host "Report saved to: $reportPath" -ForegroundColor Cyan
```

---

## Parte 12: Troubleshooting

### Problema 1: SmartScreen Ainda Bloqueia

**Sintoma:** Mesmo após importar certificado via GPO, SmartScreen mostra aviso.

**Soluções:**

```powershell
# 1. Verificar se certificado foi importado corretamente
Get-ChildItem Cert:\LocalMachine\TrustedPublisher | Where-Object { $_.Subject -like "*Paneas*" }
Get-ChildItem Cert:\LocalMachine\Root | Where-Object { $_.Subject -like "*Paneas*" }

# Se não aparecer, importar manualmente:
$certPath = "\\SERVER\Share\PaneasMonitor-CodeSigning.cer"
Import-Certificate -FilePath $certPath -CertStoreLocation Cert:\LocalMachine\TrustedPublisher
Import-Certificate -FilePath $certPath -CertStoreLocation Cert:\LocalMachine\Root

# 2. Forçar atualização do GPO
gpupdate /force

# 3. Verificar assinatura do arquivo
signtool verify /pa /v "C:\Path\To\Agent.exe"

# 4. Última opção: Desabilitar SmartScreen (não recomendado)
# Via GPO: Configure App Install Control → Turn off
```

### Problema 2: MSI Não Instala via GPO

**Sintoma:** GPO configurado mas máquinas não instalam.

**Diagnóstico:**

```powershell
# 1. Verificar se GPO está aplicado
gpresult /R /SCOPE COMPUTER

# Procurar por: "Deploy Paneas Monitor"
# Se não aparecer, GPO não está aplicado

# 2. Verificar permissões no file share
Test-Path "\\SERVER\Software\PaneasMonitor\PaneasMonitor.msi"

# 3. Verificar logs do Windows Installer
Get-WinEvent -LogName Application -MaxEvents 50 | Where-Object { $_.ProviderName -eq "MsiInstaller" }

# 4. Forçar reinstalação
msiexec /i "\\SERVER\Software\PaneasMonitor\PaneasMonitor.msi" /qn /l*v C:\Temp\install.log
```

**Soluções Comuns:**

1. **Permissões incorretas:**
   ```powershell
   # Dar Read ao Domain Computers
   $acl = Get-Acl "\\SERVER\Software\PaneasMonitor"
   $accessRule = New-Object System.Security.AccessControl.FileSystemAccessRule("Domain Computers","Read","Allow")
   $acl.AddAccessRule($accessRule)
   Set-Acl "\\SERVER\Software\PaneasMonitor" $acl
   ```

2. **GPO não linkado:**
   ```powershell
   New-GPLink -Name "Deploy Paneas Monitor" -Target "DC=domain,DC=com" -LinkEnabled Yes
   ```

3. **MSI corrupto:**
   ```powershell
   # Rebuild MSI
   cd c2-agent\PaneasMonitor.Installer
   dotnet clean
   dotnet build -c Release -p:Platform=x64
   ```

### Problema 3: Service Não Inicia

**Sintoma:** Instalação completa mas service não está rodando.

**Diagnóstico:**

```powershell
# Verificar status do service
Get-Service -Name "PaneasMonitor"

# Ver logs de erro
Get-WinEvent -LogName System -MaxEvents 50 | Where-Object { $_.ProviderName -eq "Service Control Manager" -and $_.Message -like "*Paneas*" }

# Tentar iniciar manualmente
Start-Service -Name "PaneasMonitor"

# Ver erro específico
$error[0] | Format-List -Force
```

**Soluções:**

1. **Dependências faltando:**
   ```xml
   <!-- Adicionar no ServiceInstall.wxs -->
   <ServiceInstall ...>
     <ServiceDependency Id="RpcSs"/> <!-- Remote Procedure Call -->
   </ServiceInstall>
   ```

2. **Permissões:**
   ```powershell
   # Verificar que service está rodando como LocalSystem
   # E que tem permissões nas pastas de dados
   ```

3. **Path incorreto:**
   ```powershell
   # Verificar que todos os arquivos estão no lugar
   Test-Path "C:\Program Files\Paneas Monitor\bin\Agent.exe"
   Test-Path "C:\Program Files\Paneas Monitor\ffmpeg\ffmpeg.exe"
   ```

### Problema 4: Task Scheduler Não Cria

**Sintoma:** Instalação completa mas task não aparece.

**Diagnóstico:**

```powershell
# Verificar se task existe
Get-ScheduledTask -TaskName "PaneasMonitorTask"

# Ver logs do Task Scheduler
Get-WinEvent -LogName "Microsoft-Windows-TaskScheduler/Operational" -MaxEvents 50

# Criar manualmente para testar
schtasks /CREATE /XML "C:\Program Files\Paneas Monitor\config\task-definition.xml" /TN "PaneasMonitorTask" /F
```

**Soluções:**

1. **Permissões:**
   ```xml
   <!-- Garantir que CustomAction roda como admin -->
   <CustomAction ... Impersonate="no" />
   ```

2. **XML inválido:**
   ```powershell
   # Validar XML
   [xml]$xml = Get-Content "task-definition.xml"
   ```

---

## Parte 13: Validação e Testes

### Checklist de Validação:

```powershell
# Script: validate-installation.ps1

Write-Host "=== Paneas Monitor - Validação de Instalação ===" -ForegroundColor Cyan

# 1. Arquivos instalados
Write-Host "`n[1] Verificando arquivos..." -ForegroundColor Yellow
$files = @(
    "C:\Program Files\Paneas Monitor\bin\Agent.exe",
    "C:\Program Files\Paneas Monitor\ffmpeg\ffmpeg.exe",
    "C:\Program Files\Paneas Monitor\config\appsettings.json"
)

foreach ($file in $files) {
    $exists = Test-Path $file
    $status = if ($exists) { "✓" } else { "✗" }
    $color = if ($exists) { "Green" } else { "Red" }
    Write-Host "  $status $file" -ForegroundColor $color
}

# 2. Windows Service
Write-Host "`n[2] Verificando Windows Service..." -ForegroundColor Yellow
$service = Get-Service -Name "PaneasMonitor" -ErrorAction SilentlyContinue
if ($service) {
    Write-Host "  ✓ Service encontrado: $($service.DisplayName)" -ForegroundColor Green
    Write-Host "  Status: $($service.Status)" -ForegroundColor $(if ($service.Status -eq "Running") { "Green" } else { "Yellow" })
} else {
    Write-Host "  ✗ Service não encontrado" -ForegroundColor Red
}

# 3. Task Scheduler
Write-Host "`n[3] Verificando Task Scheduler..." -ForegroundColor Yellow
$task = Get-ScheduledTask -TaskName "PaneasMonitorTask" -ErrorAction SilentlyContinue
if ($task) {
    Write-Host "  ✓ Task encontrada: $($task.TaskName)" -ForegroundColor Green
    Write-Host "  Estado: $($task.State)" -ForegroundColor $(if ($task.State -eq "Ready") { "Green" } else { "Yellow" })
} else {
    Write-Host "  ✗ Task não encontrada" -ForegroundColor Red
}

# 4. Registry Configuration
Write-Host "`n[4] Verificando configuração..." -ForegroundColor Yellow
$regKey = "HKLM:\Software\PaneasMonitor\Config"
if (Test-Path $regKey) {
    $config = Get-ItemProperty -Path $regKey
    Write-Host "  ✓ Configuração encontrada" -ForegroundColor Green
    Write-Host "    Server URL: $($config.ServerUrl)" -ForegroundColor Cyan
    Write-Host "    Mode: $($config.OperationMode)" -ForegroundColor Cyan
} else {
    Write-Host "  ✗ Configuração não encontrada" -ForegroundColor Red
}

# 5. Certificate
Write-Host "`n[5] Verificando certificado..." -ForegroundColor Yellow
$cert = Get-ChildItem Cert:\LocalMachine\TrustedPublisher | Where-Object { $_.Subject -like "*Paneas*" }
if ($cert) {
    Write-Host "  ✓ Certificado instalado" -ForegroundColor Green
    Write-Host "    Subject: $($cert.Subject)" -ForegroundColor Cyan
} else {
    Write-Host "  ⚠️  Certificado não encontrado (SmartScreen pode bloquear)" -ForegroundColor Yellow
}

# 6. Process Running
Write-Host "`n[6] Verificando processos..." -ForegroundColor Yellow
$agentProcess = Get-Process -Name "Agent" -ErrorAction SilentlyContinue
if ($agentProcess) {
    Write-Host "  ✓ Agent.exe está rodando (PID: $($agentProcess.Id))" -ForegroundColor Green
} else {
    Write-Host "  ⚠️  Agent.exe não está rodando" -ForegroundColor Yellow
}

Write-Host "`n=== Validação Concluída ===" -ForegroundColor Cyan
```

---

## Parte 14: Deployment Timeline

### Fase 1: Desenvolvimento (1 semana)

- **Dia 1-2:** Criar projeto WiX e estrutura básica
- **Dia 3-4:** Implementar componentes (files, service, task)
- **Dia 5:** Self-signed certificate e assinatura
- **Dia 6-7:** Testes em VMs

### Fase 2: Pilot (1 semana)

- **Dia 8-9:** Deploy em 10-20 máquinas de teste
- **Dia 10-12:** Monitorar issues, coletar feedback
- **Dia 13-14:** Ajustes e correções

### Fase 3: Production Rollout (2 semanas)

- **Semana 3:**
  - Deploy 10% do fleet
  - Monitor por 3-5 dias
  - Validar estabilidade

- **Semana 4:**
  - Deploy 50% do fleet
  - Deploy final 100%
  - Validação completa

---

## Resumo

### O Que Foi Criado:

✅ **Instalador MSI profissional** (WiX Toolset)
✅ **Self-signed certificate** (gratuito)
✅ **Distribuição via GPO** (automática)
✅ **Scripts PowerShell** (build, deploy, validate)
✅ **Windows Service installation** (automático)
✅ **Task Scheduler configuration** (automático)
✅ **Zero custo** (100% ferramentas gratuitas)

### Benefícios:

- 🎯 Deployment centralizado e controlado
- 🎯 Sem interação do usuário necessária
- 🎯 Sem avisos do SmartScreen (com GPO)
- 🎯 Update/uninstall via GPO
- 🎯 Escalável para centenas/milhares de máquinas

### Próximos Passos:

1. **Implementar Service + Task Hybrid** (se ainda não feito)
2. **Criar projeto WiX** seguindo este guia
3. **Criar self-signed certificate** e distribuir via GPO
4. **Build e test** em VMs
5. **Pilot deployment** em máquinas reais
6. **Production rollout** gradual

---

**Documentação criada em:** 2025-11-07
**Versão:** 1.0
**Custo Total:** $0 (FREE!)
**Tempo Estimado:** 2-4 semanas (dev to production)
