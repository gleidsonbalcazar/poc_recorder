# C2 Dashboard - Frontend Angular

Dashboard web para gerenciar agentes e enviar comandos remotos via API REST.

## 🚀 Tecnologias

- Angular 17
- TypeScript
- RxJS
- HttpClient
- Standalone Components

## 📋 Pré-requisitos

- Node.js 18+
- npm 10+
- Angular CLI 17

## ⚙️ Instalação

Já realizada durante a criação do projeto. Se precisar reinstalar:

```bash
npm install
```

## ▶️ Execução

### Modo desenvolvimento:
```bash
ng serve
```

O dashboard estará disponível em: `http://localhost:4200`

### Build de produção:
```bash
ng build --configuration production
```

Os arquivos compilados estarão em `dist/c2-dashboard/`

## 🏗️ Estrutura do Projeto

```
src/
├── app/
│   ├── components/
│   │   ├── agent-list/          # Lista de agentes conectados
│   │   ├── command-form/         # Formulário para enviar comandos
│   │   └── result-list/          # Lista de resultados
│   ├── models/
│   │   ├── agent.model.ts
│   │   ├── command.model.ts
│   │   └── result.model.ts
│   ├── services/
│   │   ├── api.service.ts        # Comunicação com API
│   │   └── agent.service.ts      # Gerenciamento de estado
│   ├── app.component.ts
│   └── app.config.ts
└── environments/
    ├── environment.ts
    └── environment.prod.ts
```

## 🔧 Configuração

### Environment

Edite `src/environments/environment.ts` para configurar a URL do servidor:

```typescript
export const environment = {
  production: false,
  apiUrl: 'http://localhost:8000'  // URL do c2-server
};
```

## 📡 Componentes

### AgentListComponent
- Exibe lista de agentes online
- Atualização automática a cada 3 segundos
- Indicadores de status (online/offline)
- Botão para selecionar agente e enviar comando

### CommandFormComponent
- Formulário para enviar comandos
- Seleção de agente
- Comandos comuns pré-configurados
- Feedback de sucesso/erro

### ResultListComponent
- Lista de resultados recentes
- Atualização automática a cada 5 segundos
- Exibição de output e erros
- Indicador de status da execução

## 🔄 Services

### ApiService
Responsável pela comunicação HTTP com o c2-server:
- `getAgents()` - Lista agentes
- `sendCommand()` - Envia comando
- `getResult()` - Consulta resultado
- `getResults()` - Lista resultados

### AgentService
Gerencia estado e polling automático:
- Observable de agentes (`agents$`)
- Observable de resultados (`results$`)
- Polling automático (3s para agentes, 5s para resultados)

## 🎨 Funcionalidades

✅ Visualização em tempo real de agentes conectados
✅ Envio de comandos para agentes específicos
✅ Comandos comuns com um clique
✅ Histórico de resultados
✅ Interface responsiva
✅ Indicadores visuais de status
✅ Auto-atualização de dados

## 🔗 Integração com c2-server

O dashboard consome a API REST do c2-server. Certifique-se de que:

1. O c2-server está rodando em `http://localhost:8000`
2. CORS está configurado no servidor para aceitar `http://localhost:4200`
3. Os endpoints estão acessíveis

## 🐛 Debug

### Erro de CORS
Se encontrar erro de CORS, verifique se o c2-server tem a configuração correta:
```python
app.add_middleware(
    CORSMiddleware,
    allow_origins=["http://localhost:4200"],
    allow_methods=["*"],
    allow_headers=["*"],
)
```

### Agentes não aparecem
1. Verifique se o c2-server está rodando
2. Abra o DevTools (F12) e veja se há erros de rede
3. Teste o endpoint manualmente: `curl http://localhost:8000/agents`

## 📝 Desenvolvimento

### Criar novo componente:
```bash
ng generate component components/nome-componente
```

### Criar novo service:
```bash
ng generate service services/nome-service
```

## 🔗 Componentes Relacionados

- **c2-server** - Servidor FastAPI (API REST + SSE)
- **c2-agent** - Agente Windows C#
