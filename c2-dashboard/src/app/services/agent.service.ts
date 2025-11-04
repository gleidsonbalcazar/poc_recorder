import { Injectable } from '@angular/core';
import { BehaviorSubject, interval, Observable } from 'rxjs';
import { switchMap, catchError } from 'rxjs/operators';
import { Agent } from '../models/agent.model';
import { Result } from '../models/result.model';
import { ApiService } from './api.service';

@Injectable({
  providedIn: 'root'
})
export class AgentService {
  private agentsSubject = new BehaviorSubject<Agent[]>([]);
  private resultsSubject = new BehaviorSubject<Result[]>([]);

  public agents$ = this.agentsSubject.asObservable();
  public results$ = this.resultsSubject.asObservable();

  private pollingInterval = 3000; // 3 seconds

  constructor(private apiService: ApiService) {
    this.startAgentPolling();
    this.startResultsPolling();
  }

  /**
   * Start polling agents every 3 seconds
   */
  private startAgentPolling(): void {
    interval(this.pollingInterval)
      .pipe(
        switchMap(() => this.apiService.getAgents()),
        catchError((error) => {
          console.error('Error fetching agents:', error);
          return [];
        })
      )
      .subscribe((response: any) => {
        if (response && response.agents) {
          this.agentsSubject.next(response.agents);
        }
      });

    // Initial fetch
    this.refreshAgents();
  }

  /**
   * Start polling results every 5 seconds
   */
  private startResultsPolling(): void {
    interval(5000)
      .pipe(
        switchMap(() => this.apiService.getResults(20)),
        catchError((error) => {
          console.error('Error fetching results:', error);
          return [];
        })
      )
      .subscribe((response: any) => {
        if (response && response.results) {
          this.resultsSubject.next(response.results);
        }
      });

    // Initial fetch
    this.refreshResults();
  }

  /**
   * Manually refresh agents list
   */
  refreshAgents(): void {
    this.apiService.getAgents().subscribe({
      next: (response) => {
        this.agentsSubject.next(response.agents);
      },
      error: (error) => {
        console.error('Error refreshing agents:', error);
      }
    });
  }

  /**
   * Manually refresh results list
   */
  refreshResults(): void {
    this.apiService.getResults(20).subscribe({
      next: (response) => {
        this.resultsSubject.next(response.results);
      },
      error: (error) => {
        console.error('Error refreshing results:', error);
      }
    });
  }

  /**
   * Get current agents
   */
  getAgents(): Agent[] {
    return this.agentsSubject.value;
  }

  /**
   * Get current results
   */
  getResults(): Result[] {
    return this.resultsSubject.value;
  }

  /**
   * Send command to agent
   */
  sendCommand(agentId: string, command: string): Observable<any> {
    return this.apiService.sendCommand({ agent_id: agentId, command });
  }
}
