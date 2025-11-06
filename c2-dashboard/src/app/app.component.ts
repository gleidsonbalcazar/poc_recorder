import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterOutlet } from '@angular/router';
import { Agent } from './models/agent.model';
import { AgentListComponent } from './components/agent-list/agent-list.component';
import { ResultListComponent } from './components/result-list/result-list.component';
import { MediaControlComponent } from './components/media-control/media-control.component';
import { MediaFilesComponent } from './components/media-files/media-files.component';
import { AgentStatusComponent } from './components/agent-status/agent-status.component';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [
    CommonModule,
    RouterOutlet,
    AgentListComponent,
    ResultListComponent,
    MediaControlComponent,
    MediaFilesComponent,
    AgentStatusComponent
  ],
  templateUrl: './app.component.html',
  styleUrl: './app.component.css'
})
export class AppComponent {
  title = 'C2 Dashboard - Command & Control System';
  selectedAgent: Agent | null = null;

  onAgentSelected(agent: Agent): void {
    this.selectedAgent = agent;
  }
}
