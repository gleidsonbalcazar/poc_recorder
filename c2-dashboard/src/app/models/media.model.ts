// Media-related models for C2 Dashboard

export interface MediaFile {
  file_path: string;
  file_name: string;
  type: 'video';
  size_bytes: number;
  size_mb: number;
  created_at: string;
  duration_minutes?: number;
}

export interface StorageStats {
  total_files: number;
  video_files: number;
  total_size_mb: number;
  video_size_mb: number;
  base_path: string;
}

export interface MediaResponse {
  agent_id: string;
  media_files: MediaFile[];
  count: number;
  storage_stats?: StorageStats;
}

export interface MediaCommand {
  agent_id: string;
  command: string;
  type?: 'video:start' | 'video:stop' | 'video:config' | 'media:list' | 'media:stats' | 'media:clean';
  duration?: number;
  fps?: number;
  quality?: number;
  interval?: number;
  days?: number;
}

export interface RecordingStatus {
  videoRecording: boolean;
  periodicRecording: boolean;
}
export interface RecordingSession {
  session_key: string;
  segment_count: number;
  total_size_bytes: number;
  total_size_mb: number;
  start_time: string;
  end_time: string;
  duration_minutes: number;
  date_folder: string;
  segments?: MediaFile[];
}

export interface SessionsResponse {
  agent_id: string;
  sessions: RecordingSession[];
  count: number;
}
