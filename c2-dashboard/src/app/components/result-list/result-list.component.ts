import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Result } from '../../models/result.model';
import { AgentService } from '../../services/agent.service';

@Component({
  selector: 'app-result-list',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './result-list.component.html',
  styleUrl: './result-list.component.css'
})
export class ResultListComponent implements OnInit {
  results: Result[] = [];

  constructor(private agentService: AgentService) {}

  ngOnInit(): void {
    this.agentService.results$.subscribe(results => {
      this.results = results;
    });
  }

  getStatusClass(status: string): string {
    switch (status) {
      case 'completed':
        return 'status-completed';
      case 'queued':
        return 'status-queued';
      case 'failed':
        return 'status-failed';
      default:
        return '';
    }
  }

  formatTimestamp(timestamp: string): string {
    const date = new Date(timestamp);
    return date.toLocaleString('pt-BR');
  }

  truncateOutput(output: string, maxLength: number = 200): string {
    if (output.length <= maxLength) {
      return output;
    }
    return output.substring(0, maxLength) + '...';
  }
}
