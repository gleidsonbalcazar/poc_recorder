export interface Result {
  task_id: string;
  agent_id: string;
  command: string;
  output: string;
  error?: string | null;
  exit_code: number;
  timestamp: string;
  status: 'queued' | 'completed' | 'failed';
}

export interface ResultsResponse {
  results: Result[];
  count: number;
}
