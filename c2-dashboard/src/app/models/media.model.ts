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
