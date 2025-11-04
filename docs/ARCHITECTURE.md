# Architecture - POC Sistema C2 com SSE

Documentação da arquitetura do sistema de comando e controle.

## Visão Geral

```
┌─────────────────┐         ┌──────────────────┐         ┌─────────────────┐
│  Dashboard Web  │         │  Servidor Python │         │  Agente Windows │
│   (Angular 17)  │         │    (FastAPI)     │         │     (C# .NET)   │
└────────┬────────┘         └────────┬─────────┘         └────────┬────────┘
         │                           │                            │
         │ HTTP REST API             │                            │
         │◄─────────────────────────►│                            │
         │                           │                            │
         │                           │ SSE (Server-Sent Events)   │
         │                           │◄───────────────────────────│
         │                           │                            │
```

---

## Componentes

### 1. Dashboard Web (c2-dashboard)

**Tecnologias:**
- Angular 17 (Standalone Components)
- TypeScript
- RxJS
- HttpClient

**Responsabilidades:**
- Interface web para visualização e controle
- Listagem de agentes online
- Envio de comandos
- Visualização de resultados
- Polling automático de dados

---

### 2. Servidor Python (c2-server)

**Tecnologias:**
- Python 3.8+
- FastAPI
- Uvicorn (ASGI Server)

**Responsabilidades:**
- API REST para dashboard
- Gerenciamento de conexões SSE
- Fila de comandos por agente
- Armazenamento em memória de resultados
- CORS para Angular

---

### 3. Agente Windows (c2-agent)

**Tecnologias:**
- C# / .NET 10
- HttpClient
- Process API

**Responsabilidades:**
- Conexão persistente via SSE
- Execução de comandos (cmd.exe)
- Envio de resultados
- Reconexão automática
- Logging

---

## Segurança (POC)

### Limitações Conhecidas

Esta é uma **Proof of Concept** sem implementações de segurança:

- Sem autenticação de agentes
- Sem criptografia (HTTPS)
- Sem validação de comandos
- Sem autorização
- Sem rate limiting

### Para Produção

Implementações necessárias:

1. **Autenticação:** API Keys, JWT tokens, Mutual TLS
2. **Criptografia:** HTTPS obrigatório, TLS 1.3+
3. **Validação:** Whitelist de comandos, sanitização
4. **Auditoria:** Logs persistentes, rastreamento
5. **Isolamento:** Sandbox, privilégios mínimos

---

## Tecnologias por Camada

| Camada | Tecnologia | Responsabilidade |
|--------|------------|------------------|
| Frontend | Angular 17 | Interface do usuário |
| Backend | FastAPI | API REST |
| Agent | C# .NET 10 | Execução de comandos |
| Comunicação | HTTP REST / SSE | Troca de mensagens |

---

## Portas e URLs

| Componente | Porta | URL |
|------------|-------|-----|
| c2-server | 8000 | http://localhost:8000 |
| c2-dashboard | 4200 | http://localhost:4200 |
| c2-agent | - | Conecta em :8000 |
