import { Component, OnInit, OnDestroy, Input, Output, EventEmitter } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Agent } from '../../models/agent.model';
import { AgentService } from '../../services/agent.service';
import { ApiService } from '../../services/api.service';
import { interval, Subscription } from 'rxjs';

@Component({
  selector: 'app-agent-list',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './agent-list.component.html',
  styleUrl: './agent-list.component.css'
})
export class AgentListComponent implements OnInit, OnDestroy {
  agents: Agent[] = [];
  @Input() selectedAgent: Agent | null = null;
  @Output() selectAgent = new EventEmitter<Agent>();

  private statusPollingSubscription?: Subscription;
  private pendingStatusQueries: Map<string, string> = new Map(); // agent_id -> task_id

  constructor(
    private agentService: AgentService,
    private apiService: ApiService
  ) {}

  ngOnInit(): void {
    this.agentService.agents$.subscribe(agents => {
      this.agents = agents;
      // Fetch recording status for online agents
      this.fetchRecordingStatus();
    });

    // Poll for recording status every 5 seconds
    this.statusPollingSubscription = interval(5000).subscribe(() => {
      this.fetchRecordingStatus();
    });
  }

  ngOnDestroy(): void {
    this.statusPollingSubscription?.unsubscribe();
  }

  private fetchRecordingStatus(): void {
    this.agents.forEach(agent => {
      if (agent.status === 'online') {
        const pendingTaskId = this.pendingStatusQueries.get(agent.agent_id);

        if (pendingTaskId) {
          // Check if previous query completed
          this.apiService.getAgentStatusFromResult(pendingTaskId).subscribe({
            next: (status) => {
              if (status) {
                agent.is_recording = status.recording.is_recording;
                agent.recording_session = status.recording.session_key;
                this.pendingStatusQueries.delete(agent.agent_id);
              }
            },
            error: () => {
              // Task not complete yet, will retry later
            }
          });
        } else {
          // Send new status query
          this.apiService.sendMediaCommand(agent.agent_id, 'status:query').subscribe({
            next: (response) => {
              if (response && response.task_id) {
                this.pendingStatusQueries.set(agent.agent_id, response.task_id);
              }
            },
            error: () => {
              // Failed to send command
            }
          });
        }
      }
    });
  }

  onSelectAgent(agent: Agent): void {
    this.selectAgent.emit(agent);
  }

  getStatusClass(status: string): string {
    switch (status) {
      case 'online':
        return 'status-online';
      case 'offline':
        return 'status-offline';
      default:
        return 'status-disconnected';
    }
  }

  getTimeSince(timestamp: string): string {
    const now = new Date().getTime();
    const then = new Date(timestamp).getTime();
    const diffSeconds = Math.floor((now - then) / 1000);

    if (diffSeconds < 60) {
      return `há ${diffSeconds} segundos`;
    } else if (diffSeconds < 3600) {
      const minutes = Math.floor(diffSeconds / 60);
      return `há ${minutes} minuto${minutes > 1 ? 's' : ''}`;
    } else {
      const hours = Math.floor(diffSeconds / 3600);
      return `há ${hours} hora${hours > 1 ? 's' : ''}`;
    }
  }
}
