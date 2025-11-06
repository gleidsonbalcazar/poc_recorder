import { AgentStatus } from './status.model';

export interface Result {
  task_id: string;
  agent_id: string;
  command: string;
  output: string;
  error?: string | null;
  exit_code: number;
  timestamp: string;
  status: 'queued' | 'completed' | 'failed';
  agent_status?: AgentStatus | null;
}

export interface ResultsResponse {
  results: Result[];
  count: number;
}
