import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { Agent } from '../models/agent.model';
import { CommandRequest, CommandResponse } from '../models/command.model';
import { Result, ResultsResponse } from '../models/result.model';
import { MediaResponse, MediaCommand, SessionsResponse, RecordingSession } from '../models/media.model';
import { AgentStatus } from '../models/status.model';
import { environment } from '../../environments/environment';

@Injectable({
  providedIn: 'root'
})
export class ApiService {
  private apiUrl = environment.apiUrl;

  constructor(private http: HttpClient) { }

  /**
   * Get list of all connected agents
   */
  getAgents(): Observable<{ agents: Agent[], count: number }> {
    return this.http.get<{ agents: Agent[], count: number }>(`${this.apiUrl}/agents`);
  }

  /**
   * Send command to a specific agent
   */
  sendCommand(commandRequest: CommandRequest): Observable<CommandResponse> {
    return this.http.post<CommandResponse>(`${this.apiUrl}/command`, commandRequest);
  }

  /**
   * Get result of a specific task
   */
  getResult(taskId: string): Observable<Result> {
    return this.http.get<Result>(`${this.apiUrl}/result/${taskId}`);
  }

  /**
   * Get list of recent results
   */
  getResults(limit: number = 50): Observable<ResultsResponse> {
    return this.http.get<ResultsResponse>(`${this.apiUrl}/results?limit=${limit}`);
  }

  /**
   * Remove agent from server
   */
  removeAgent(agentId: string): Observable<any> {
    return this.http.delete(`${this.apiUrl}/agent/${agentId}`);
  }

  /**
   * Get server status
   */
  getServerStatus(): Observable<any> {
    return this.http.get(`${this.apiUrl}/`);
  }

  // ===== MEDIA METHODS =====

  /**
   * Get media files and storage statistics for a specific agent
   */
  getAgentMedia(agentId: string): Observable<MediaResponse> {
    return this.http.get<MediaResponse>(`${this.apiUrl}/media/${agentId}`);
  }

  /**
   * Send media command to agent (video recording)
   */
  sendMediaCommand(agentId: string, command: string): Observable<CommandResponse> {
    return this.sendCommand({ agent_id: agentId, command });
  }

  /**
   * Start video recording
   */
  startVideoRecording(agentId: string, duration?: number, fps?: number): Observable<CommandResponse> {
    let command = 'video:start';
    if (duration) command += ` ${duration}`;
    if (fps) command += ` ${fps}`;
    return this.sendMediaCommand(agentId, command);
  }

  /**
   * Stop video recording
   */
  stopVideoRecording(agentId: string): Observable<CommandResponse> {
    return this.sendMediaCommand(agentId, 'video:stop');
  }

  /**
   * Configure periodic video recording
   */
  configPeriodicVideo(agentId: string, interval: number, duration: number): Observable<CommandResponse> {
    return this.sendMediaCommand(agentId, `video:config ${interval} ${duration}`);
  }

  /**
   * List media files
   */
  listMediaFiles(agentId: string): Observable<CommandResponse> {
    return this.sendMediaCommand(agentId, 'media:list');
  }

  /**
   * Get storage statistics
   */
  getStorageStats(agentId: string): Observable<CommandResponse> {
    return this.sendMediaCommand(agentId, 'media:stats');
  }

  /**
   * Clean old media files
   */
  cleanOldFiles(agentId: string, days: number = 7): Observable<CommandResponse> {
    return this.sendMediaCommand(agentId, `media:clean ${days}`);
  }

  /**
   * Delete a specific media file
   */
  deleteMediaFile(agentId: string, filename: string): Observable<CommandResponse> {
    return this.sendMediaCommand(agentId, `media:delete ${filename}`);
  }

  /**
   * Get preview URL for a media file
   */
  getPreviewUrl(agentId: string, filename: string): Observable<{ url: string, filename: string, agent_id: string }> {
    return this.http.get<{ url: string, filename: string, agent_id: string }>(
      `${this.apiUrl}/media/preview/${agentId}/${encodeURIComponent(filename)}`
    );
  }

  // ===== SESSION METHODS =====

  /**
   * Get recording sessions for a specific agent
   */
  getAgentSessions(agentId: string): Observable<SessionsResponse> {
    return this.http.get<SessionsResponse>(`${this.apiUrl}/media/${agentId}/sessions`);
  }

  /**
   * Get details of a specific recording session
   */
  getSessionDetails(agentId: string, sessionKey: string): Observable<RecordingSession> {
    return this.http.get<RecordingSession>(`${this.apiUrl}/media/${agentId}/session/${sessionKey}`);
  }

  // ===== STATUS METHODS =====

  /**
   * Get agent status (recording, database, upload, system info)
   * Sends status:query command and retrieves the result
   */
  getAgentStatus(agentId: string): Observable<AgentStatus | null> {
    return this.sendMediaCommand(agentId, 'status:query').pipe(
      map(response => {
        // Command was queued, now we need to get the result
        return null; // Will be fetched separately by polling the result
      })
    );
  }

  /**
   * Get agent status from a completed status:query result
   */
  getAgentStatusFromResult(taskId: string): Observable<AgentStatus | null> {
    return this.getResult(taskId).pipe(
      map(result => result.agent_status || null)
    );
  }
}
