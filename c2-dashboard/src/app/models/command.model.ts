export interface CommandRequest {
  agent_id: string;
  command: string;
}

export interface CommandResponse {
  task_id: string;
  agent_id: string;
  command: string;
  status: string;
}
