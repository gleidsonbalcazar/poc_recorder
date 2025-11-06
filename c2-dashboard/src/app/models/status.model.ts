export interface RecordingStatus {
  is_recording: boolean;
  session_key?: string | null;
  started_at?: string | null;
  duration_seconds: number;
  segment_count: number;
  current_file?: string | null;
  mode: string;
}

export interface DatabaseStats {
  pending: number;
  uploading: number;
  done: number;
  error: number;
  total_size_mb: number;
}

export interface UploadStatus {
  enabled: boolean;
  active_uploads: number;
  endpoint?: string | null;
}

export interface SystemInfo {
  os_version: string;
  storage_path: string;
  disk_space_gb: number;
}

export interface AgentStatus {
  recording: RecordingStatus;
  database: DatabaseStats;
  upload: UploadStatus;
  system: SystemInfo;
}
