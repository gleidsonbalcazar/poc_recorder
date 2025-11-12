# Agent.exe - Build Modes

O Agent.exe agora suporta dois modos de execução através de configurações de build Debug/Release.

## 🎯 Modos Disponíveis

### 1. Debug Mode (Console Visível)
- **OutputType**: `Exe` (Console Application)
- **Uso**: Desenvolvimento, testes, debugging
- **Comportamento**: Janela de console aparece com todas as mensagens

**Build:**
```bash
dotnet build -c Debug
# ou simplesmente
dotnet run
```

**Executável:** `bin\x64\Debug\net8.0\Agent.exe`

---

### 2. Release Mode (Sem Janela - Windowless)
- **OutputType**: `WinExe` (Windows Application)
- **Uso**: Produção, Windows Service, Task Scheduler
- **Comportamento**: Roda em background, sem janela visível

**Build:**
```bash
dotnet build -c Release
```

**Executável:** `bin\x64\Release\net8.0\win-x64\Agent.exe`

**Publish (Single File):**
```bash
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

**Executável:** `bin\x64\Release\net8.0\win-x64\publish\Agent.exe`

---

## 📋 Quando Usar Cada Modo

| Cenário | Modo | Build Command |
|---------|------|---------------|
| Teste local | Debug | `dotnet run` |
| Debug com breakpoints | Debug | VS/Rider Debug |
| Instalação via PaneasMonitorService | Release | `dotnet publish -c Release` |
| Execução via Task Scheduler | Release | `dotnet publish -c Release` |
| Deploy para produção | Release | `dotnet publish -c Release` |

---

## 🔧 Configuração Técnica

### Agent.csproj
```xml
<!-- Debug: Console Application (for testing/debugging) -->
<PropertyGroup Condition="'$(Configuration)' == 'Debug'">
  <OutputType>Exe</OutputType>
</PropertyGroup>

<!-- Release: Windows Application (no console window, for production/service) -->
<PropertyGroup Condition="'$(Configuration)' == 'Release'">
  <OutputType>WinExe</OutputType>
</PropertyGroup>
```

---

## 📝 Logging

**Console Output:**
- **Debug**: Todas as mensagens `Console.WriteLine()` aparecem na janela de console
- **Release**: `Console.WriteLine()` não causa erro, mas output é descartado (sem janela)

**Recomendação:**
Para produção (Release), configure logging em arquivo:
- Logs são salvos automaticamente em `%LOCALAPPDATA%\C2Agent\Logs\`
- Use a infraestrutura de logging existente
- Console output continua funcionando em Debug

---

## 🧪 Testar os Modos

### Testar Debug (com console):
```bash
cd c2-agent/Agent
dotnet run
```
✅ Deve abrir janela de console com mensagens

### Testar Release (sem janela):
```bash
cd c2-agent/Agent
dotnet build -c Release
.\bin\x64\Release\net8.0\win-x64\Agent.exe
```
✅ NÃO deve abrir janela de console
✅ Processo roda em background
✅ Verifique Task Manager: `Agent.exe` aparece mas sem janela

---

## 🔄 Impacto nos Scripts

### PaneasMonitorService
**Nenhuma mudança necessária!**

O serviço já executa o Agent.exe corretamente:
```csharp
// MonitorService.cs
td.Actions.Add(new ExecAction(_agentExecutablePath, null, ...));
```

### install-service.ps1
**Nenhuma mudança necessária!**

O script de instalação usa o executável Release automaticamente.

### Scripts de teste
- **test-console.ps1**: Continua funcionando (executa Agent.exe)
- **debug-scenarios.ps1**: Continua funcionando

---

## ⚠️ Notas Importantes

1. **Always build Release for production**
   - PaneasMonitorService instala o build Release
   - Task Scheduler usa build Release
   - Deploy sempre usa `dotnet publish -c Release`

2. **Debug build not for production**
   - Console window é visível para o usuário
   - Não deve ser usado em ambientes de produção

3. **Logs são essenciais em Release**
   - Sem console, logs são a única forma de debug
   - Monitore `%LOCALAPPDATA%\C2Agent\Logs\`

4. **Console.WriteLine continua funcionando**
   - Não precisa mudar código
   - Em Release, output é silenciosamente descartado
   - Em Debug, output aparece no console

---

## 🚀 Build de Produção Completo

```bash
# 1. Limpar builds anteriores
dotnet clean

# 2. Build Release com publish single-file
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true

# 3. Executável final
# Localização: bin\x64\Release\net8.0\win-x64\publish\Agent.exe
# Tamanho: ~34MB (single file)
# Modo: Windowless (WinExe)

# 4. Copiar para instalação do serviço
# O install-service.ps1 faz isso automaticamente
```

---

## 📚 Referências

- [.NET OutputType Documentation](https://learn.microsoft.com/en-us/dotnet/core/project-sdk/msbuild-props#outputtype)
- `Exe` = Console Application (with console window)
- `WinExe` = Windows Application (no console window)
