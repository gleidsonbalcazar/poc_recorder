import { Component, OnInit, Input, Output, EventEmitter } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Agent } from '../../models/agent.model';
import { AgentService } from '../../services/agent.service';

@Component({
  selector: 'app-agent-list',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './agent-list.component.html',
  styleUrl: './agent-list.component.css'
})
export class AgentListComponent implements OnInit {
  agents: Agent[] = [];
  @Input() selectedAgent: Agent | null = null;
  @Output() selectAgent = new EventEmitter<Agent>();

  constructor(private agentService: AgentService) {}

  ngOnInit(): void {
    this.agentService.agents$.subscribe(agents => {
      this.agents = agents;
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
