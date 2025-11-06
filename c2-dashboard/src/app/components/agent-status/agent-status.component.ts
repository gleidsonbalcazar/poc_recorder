import { Component, Input, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ApiService } from '../../services/api.service';
import { AgentStatus } from '../../models/status.model';
import { interval, Subscription, switchMap, catchError, of } from 'rxjs';

@Component({
  selector: 'app-agent-status',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './agent-status.component.html',
  styleUrl: './agent-status.component.css'
})
export class AgentStatusComponent implements OnInit, OnDestroy {
  @Input() agentId!: string;

  status: AgentStatus | null = null;
  loading: boolean = false;
  error: string | null = null;
  lastUpdate: Date | null = null;
  private refreshSubscription?: Subscription;
  private pendingTaskId?: string;

  constructor(private apiService: ApiService) {}

  ngOnInit(): void {
    this.fetchStatus();
    // Auto-refresh every 3 seconds
    this.refreshSubscription = interval(3000).subscribe(() => {
      this.fetchStatus();
    });
  }

  ngOnDestroy(): void {
    this.refreshSubscription?.unsubscribe();
  }

  fetchStatus(): void {
    if (!this.agentId) return;

    if (this.pendingTaskId) {
      // Check if previous status:query completed
      this.apiService.getAgentStatusFromResult(this.pendingTaskId)
        .pipe(
          catchError(err => {
            // Task might not be complete yet
            return of(null);
          })
        )
        .subscribe(status => {
          if (status) {
            this.status = status;
            this.lastUpdate = new Date();
            this.error = null;
            this.pendingTaskId = undefined;
          } else {
            // Task not complete, will retry on next interval
          }
        });
    } else {
      // Send new status:query command
      this.apiService.sendMediaCommand(this.agentId, 'status:query')
        .pipe(
          catchError(err => {
            this.error = 'Failed to request status';
            return of(null);
          })
        )
        .subscribe(response => {
          if (response) {
            this.pendingTaskId = response.task_id;
            // Will check result on next interval
          }
        });
    }
  }

  formatDuration(seconds: number): string {
    if (seconds < 60) return `${seconds}s`;
    const minutes = Math.floor(seconds / 60);
    const secs = seconds % 60;
    if (minutes < 60) return `${minutes}m ${secs}s`;
    const hours = Math.floor(minutes / 60);
    const mins = minutes % 60;
    return `${hours}h ${mins}m`;
  }

  formatSize(mb: number): string {
    if (mb < 1024) return `${mb.toFixed(2)} MB`;
    return `${(mb / 1024).toFixed(2)} GB`;
  }

  formatDateTime(dateStr: string | null | undefined): string {
    if (!dateStr) return 'N/A';
    const date = new Date(dateStr);
    return date.toLocaleString('pt-BR', {
      day: '2-digit',
      month: '2-digit',
      year: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit'
    });
  }

  getRecordingStatusClass(): string {
    if (!this.status) return 'status-badge status-unknown';
    return this.status.recording.is_recording ? 'status-badge status-recording' : 'status-badge status-stopped';
  }

  getUploadStatusClass(): string {
    if (!this.status) return 'status-badge status-unknown';
    return this.status.upload.enabled ? 'status-badge status-enabled' : 'status-badge status-disabled';
  }
}
