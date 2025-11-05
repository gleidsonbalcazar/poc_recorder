import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../services/api.service';
import { RecordingStatus } from '../../models/media.model';

@Component({
  selector: 'app-media-control',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './media-control.component.html',
  styleUrls: ['./media-control.component.css']
})
export class MediaControlComponent {
  @Input() agentId: string = '';

  recordingStatus: RecordingStatus = {
    videoRecording: false,
    periodicRecording: false
  };

  // Video settings
  videoDuration: number = 60;
  videoFps: number = 20;

  // Periodic recording settings
  periodicInterval: number = 10;
  periodicDuration: number = 5;

  // Logs
  logs: string[] = [];
  maxLogs: number = 10;

  constructor(private apiService: ApiService) {}

  // ===== VIDEO RECORDING =====

  startVideoRecording() {
    if (!this.agentId) {
      this.addLog('❌ No agent selected');
      return;
    }

    this.addLog(`🎥 Starting video recording (${this.videoDuration}s, ${this.videoFps} FPS)...`);

    this.apiService.startVideoRecording(this.agentId, this.videoDuration, this.videoFps).subscribe({
      next: (response) => {
        this.recordingStatus.videoRecording = true;
        this.addLog(`✅ Video recording started: Task ${response.task_id}`);

        // Auto-stop after duration if specified (DISABLED - allows manual control)
        /*
        if (this.videoDuration > 0) {
          setTimeout(() => {
            this.recordingStatus.videoRecording = false;
            this.addLog('🎥 Video recording completed');
          }, this.videoDuration * 1000);
        }
        */
      },
      error: (error) => {
        this.addLog(`❌ Failed to start video recording: ${error.message}`);
      }
    });
  }

  stopVideoRecording() {
    if (!this.agentId) {
      this.addLog('❌ No agent selected');
      return;
    }

    this.addLog('⏹️ Stopping video recording...');

    this.apiService.stopVideoRecording(this.agentId).subscribe({
      next: (response) => {
        this.recordingStatus.videoRecording = false;
        this.addLog(`✅ Video recording stopped: Task ${response.task_id}`);
      },
      error: (error) => {
        this.addLog(`❌ Failed to stop video recording: ${error.message}`);
      }
    });
  }

  startPeriodicRecording() {
    if (!this.agentId) {
      this.addLog('❌ No agent selected');
      return;
    }

    this.addLog(`🔄 Configuring periodic recording: ${this.periodicDuration}min every ${this.periodicInterval}min...`);

    this.apiService.configPeriodicVideo(this.agentId, this.periodicInterval, this.periodicDuration).subscribe({
      next: (response) => {
        this.recordingStatus.periodicRecording = true;
        this.addLog(`✅ Periodic recording configured: Task ${response.task_id}`);
      },
      error: (error) => {
        this.addLog(`❌ Failed to configure periodic recording: ${error.message}`);
      }
    });
  }

  stopPeriodicRecording() {
    // Send video:stop to stop periodic recording
    this.stopVideoRecording();
    this.recordingStatus.periodicRecording = false;
    this.addLog('🔄 Periodic recording stopped');
  }

  // ===== HELPERS =====

  addLog(message: string) {
    const timestamp = new Date().toLocaleTimeString();
    this.logs.unshift(`[${timestamp}] ${message}`);

    // Keep only last N logs
    if (this.logs.length > this.maxLogs) {
      this.logs = this.logs.slice(0, this.maxLogs);
    }
  }

  clearLogs() {
    this.logs = [];
  }
}
