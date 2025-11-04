import { Component, Input, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../services/api.service';
import { MediaFile, StorageStats } from '../../models/media.model';
import { MediaPreviewComponent } from '../media-preview/media-preview.component';
import { interval, Subscription } from 'rxjs';

@Component({
  selector: 'app-media-files',
  standalone: true,
  imports: [CommonModule, FormsModule, MediaPreviewComponent],
  templateUrl: './media-files.component.html',
  styleUrls: ['./media-files.component.css']
})
export class MediaFilesComponent implements OnInit, OnDestroy {
  @Input() agentId: string = '';

  mediaFiles: MediaFile[] = [];
  storageStats: StorageStats | null = null;
  loading: boolean = false;
  lastUpdate: string = '';
  cleanDays: number = 7;

  // Auto-refresh
  private refreshSubscription?: Subscription;
  autoRefresh: boolean = false;
  refreshInterval: number = 10; // seconds

  // Preview modal
  showPreview: boolean = false;
  previewUrl: string = '';
  previewType: 'video' = 'video';
  previewFileName: string = '';
  previewFileSize: number = 0;
  previewDuration: number | undefined;

  constructor(private apiService: ApiService) {}

  ngOnInit() {
    this.loadMediaFiles();
  }

  ngOnDestroy() {
    this.stopAutoRefresh();
  }

  loadMediaFiles() {
    if (!this.agentId) {
      return;
    }

    this.loading = true;

    this.apiService.getAgentMedia(this.agentId).subscribe({
      next: (response) => {
        this.mediaFiles = response.media_files;
        this.storageStats = response.storage_stats || null;
        this.lastUpdate = new Date().toLocaleTimeString();
        this.loading = false;
      },
      error: (error) => {
        console.error('Error loading media files:', error);
        this.loading = false;
      }
    });
  }

  requestMediaList() {
    if (!this.agentId) {
      return;
    }

    // Send media:list command to agent
    this.apiService.listMediaFiles(this.agentId).subscribe({
      next: (response) => {
        console.log('Media list command sent:', response.task_id);
        // Refresh after a delay to get updated results
        setTimeout(() => this.loadMediaFiles(), 3000);
      },
      error: (error) => {
        console.error('Error sending media list command:', error);
      }
    });
  }

  requestStorageStats() {
    if (!this.agentId) {
      return;
    }

    // Send media:stats command to agent
    this.apiService.getStorageStats(this.agentId).subscribe({
      next: (response) => {
        console.log('Storage stats command sent:', response.task_id);
        // Refresh after a delay to get updated results
        setTimeout(() => this.loadMediaFiles(), 3000);
      },
      error: (error) => {
        console.error('Error sending storage stats command:', error);
      }
    });
  }

  cleanOldFiles() {
    if (!this.agentId) {
      return;
    }

    if (!confirm(`Delete files older than ${this.cleanDays} days?`)) {
      return;
    }

    this.apiService.cleanOldFiles(this.agentId, this.cleanDays).subscribe({
      next: (response) => {
        console.log('Clean files command sent:', response.task_id);
        alert(`Clean command sent. Check results after execution.`);
        // Refresh after a delay
        setTimeout(() => this.loadMediaFiles(), 3000);
      },
      error: (error) => {
        console.error('Error sending clean command:', error);
        alert('Failed to send clean command');
      }
    });
  }

  toggleAutoRefresh() {
    this.autoRefresh = !this.autoRefresh;

    if (this.autoRefresh) {
      this.startAutoRefresh();
    } else {
      this.stopAutoRefresh();
    }
  }

  private startAutoRefresh() {
    this.refreshSubscription = interval(this.refreshInterval * 1000).subscribe(() => {
      this.loadMediaFiles();
    });
  }

  private stopAutoRefresh() {
    if (this.refreshSubscription) {
      this.refreshSubscription.unsubscribe();
      this.refreshSubscription = undefined;
    }
  }

  getFileIcon(type: string): string {
    return '🎥';
  }

  formatDate(dateString: string): string {
    try {
      const date = new Date(dateString);
      return date.toLocaleString();
    } catch {
      return dateString;
    }
  }

  formatDuration(minutes?: number): string {
    if (!minutes) return 'N/A';

    if (minutes < 1) {
      return `${Math.round(minutes * 60)}s`;
    }

    return `${minutes.toFixed(1)}min`;
  }

  openPreview(file: MediaFile) {
    if (!this.agentId) {
      alert('No agent selected');
      return;
    }

    // Request preview URL from server
    this.apiService.getPreviewUrl(this.agentId, file.file_name).subscribe({
      next: (response) => {
        this.previewUrl = response.url;
        this.previewType = 'video';
        this.previewFileName = file.file_name;
        this.previewFileSize = file.size_bytes;
        this.previewDuration = file.duration_minutes;
        this.showPreview = true;

        console.log('Opening preview:', response);
      },
      error: (error) => {
        console.error('Error getting preview URL:', error);
        alert('Failed to get preview URL. Make sure the agent is running.');
      }
    });
  }

  closePreview() {
    this.showPreview = false;
    this.previewUrl = '';
  }

  deleteFile(file: MediaFile) {
    if (!this.agentId) {
      alert('No agent selected');
      return;
    }

    if (!confirm(`Delete file "${file.file_name}"?\n\nThis action cannot be undone.`)) {
      return;
    }

    this.apiService.deleteMediaFile(this.agentId, file.file_name).subscribe({
      next: (response) => {
        console.log('Delete command sent:', response.task_id);
        alert(`Delete command sent. Refreshing file list...`);
        // Refresh after a delay to allow deletion to complete
        setTimeout(() => this.loadMediaFiles(), 2000);
      },
      error: (error) => {
        console.error('Error sending delete command:', error);
        alert('Failed to send delete command');
      }
    });
  }
}
