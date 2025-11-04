# POC - Sistema C2 (Command & Control) com SSE

Sistema de comando e controle onde um dashboard web envia comandos para agentes Windows via Server-Sent Events (SSE).

**⚠️ IMPORTANTE: Projeto educacional/POC. Não usar em produção!**

## 📋 Visão Geral

Este projeto demonstra comunicação em tempo real entre:
- **Dashboard Web (Angular)** → envia comandos
- **Servidor Python (FastAPI)** → gerencia comunicação via SSE
- **Agente Windows (C#)** → executa comandos e retorna resultados

## 🏗️ Arquitetura

```
┌─────────────────┐         ┌──────────────────┐         ┌─────────────────┐
│  Dashboard Web  │         │  Servidor Python │         │  Agente Windows │
│   (Angular 17)  │◄───────►│    (FastAPI)     │◄───────►│     (C# .NET)   │
└─────────────────┘         └──────────────────┘         └─────────────────┘
  localhost:4200              localhost:8000              Conecta via SSE
```

## 📁 Estrutura do Projeto

```
Pan/
├── c2-server/          # Servidor Python (FastAPI)
├── c2-dashboard/       # Dashboard Angular 17
├── c2-agent/           # Agente Windows (C# .NET 10)
├── docs/               # Documentação
│   ├── API.md
│   └── ARCHITECTURE.md
├── CLAUDE.md           # Guia para Claude Code
└── README.md           # Este arquivo
```

## 🚀 Quick Start

### 1. Servidor Python

```bash
cd c2-server
python -m venv venv
venv\Scripts\activate  # Windows
pip install -r requirements.txt
uvicorn server:app --reload --host localhost --port 8000
```

Acesse: http://localhost:8000

### 2. Dashboard Angular

```bash
cd c2-dashboard
npm install  # Se necessário
ng serve
```

Acesse: http://localhost:4200

### 3. Agente Windows

```bash
cd c2-agent/Agent
dotnet run
```

O agente conectará automaticamente em http://localhost:8000

## ✨ Funcionalidades

✅ Visualização de agentes online em tempo real
✅ Envio de comandos para agentes específicos
✅ Execução remota de comandos Windows (cmd.exe)
✅ Retorno de resultados em tempo real
✅ Reconexão automática do agente
✅ Interface web intuitiva
✅ Múltiplos agentes simultâneos

## 🎯 Comandos de Teste

Após iniciar todos os componentes, teste com:

- `whoami` - Identidade do usuário
- `hostname` - Nome do computador
- `ipconfig` - Configuração de rede
- `dir C:\` - Listar diretório
- `systeminfo` - Informações do sistema

## 📚 Documentação

- [API.md](docs/API.md) - Documentação completa da API REST
- [ARCHITECTURE.md](docs/ARCHITECTURE.md) - Arquitetura do sistema
- [c2-server/README.md](c2-server/README.md) - Servidor Python
- [c2-dashboard/README.md](c2-dashboard/README.md) - Dashboard Angular
- [c2-agent/README.md](c2-agent/README.md) - Agente Windows

## 🔧 Stack Tecnológica

| Componente | Tecnologia | Porta |
|------------|-----------|-------|
| Servidor | Python 3.8+ + FastAPI | 8000 |
| Dashboard | Angular 17 + TypeScript | 4200 |
| Agente | C# .NET 10 | - |
| Comunicação | HTTP REST + SSE | - |

## 🔒 Segurança

**⚠️ Este é um projeto POC sem implementações de segurança:**

**Falta:**
- ❌ Autenticação
- ❌ Criptografia (HTTPS)
- ❌ Validação de comandos
- ❌ Autorização
- ❌ Rate limiting

**Não use em produção!**

Para detalhes sobre segurança e melhorias necessárias, veja [ARCHITECTURE.md](docs/ARCHITECTURE.md).

## 🐛 Troubleshooting

### Servidor não inicia
- Verificar se porta 8000 está livre
- Verificar se Python 3.8+ está instalado
- Reinstalar dependências: `pip install -r requirements.txt`

### Dashboard não conecta
- Verificar se servidor está rodando em http://localhost:8000
- Verificar CORS no servidor
- Abrir DevTools (F12) e verificar erros

### Agente não aparece
- Verificar se servidor está rodando
- Verificar logs do agente
- Testar: `curl http://localhost:8000/agents`

## 📝 Notas

- Servidor armazena dados em **memória** (reiniciar = perda de dados)
- Agente executa **qualquer comando** recebido (sem validação)
- Dashboard usa **polling** (não WebSocket)
- **Sem persistência** de histórico

## 🎓 Propósito

Este projeto é uma **Proof of Concept educacional** para demonstrar:
- Comunicação via Server-Sent Events (SSE)
- Arquitetura de sistemas distribuídos
- Integração de tecnologias (Python, Angular, C#)
- Padrões de comunicação cliente-servidor

## 📄 Licença

Este projeto é fornecido "como está" para fins educacionais.

---

**Desenvolvido como POC - Sistema C2 com SSE**
