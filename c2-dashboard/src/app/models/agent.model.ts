export interface Agent {
  agent_id: string;
  hostname: string;
  connected_at: string;
  last_seen: string;
  status: 'online' | 'offline' | 'disconnected';
  // Optional recording status (fetched separately)
  is_recording?: boolean;
  recording_session?: string | null;
}
