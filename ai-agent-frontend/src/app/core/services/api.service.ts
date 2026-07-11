import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
  AuthResponse, LoginRequest, RegisterRequest,
  TaskItem, CreateTaskRequest, UpdateTaskRequest,
  CalendarEvent, CreateEventRequest,
  ChatMessage, SendMessageResponse, UserStats,
  IntegrationStatus, MessagingStatus, UpdateProfileRequest, User, MessagingPreference,
  AutomationRule, AutomationTemplate, DocumentInfo, Team,
  TrelloStatus, CalendarSyncStatus
} from '../models/models';

@Injectable({ providedIn: 'root' })
export class ApiService {
  private api = environment.apiUrl;

  constructor(private http: HttpClient) {}

  // Auth
  login(req: LoginRequest): Observable<AuthResponse> {
    return this.http.post<AuthResponse>(`${this.api}/api/Auth/login`, req);
  }
  register(req: RegisterRequest): Observable<AuthResponse> {
    return this.http.post<AuthResponse>(`${this.api}/api/Auth/register`, req);
  }
  refreshToken(token: string): Observable<{ token: string }> {
    return this.http.post<{ token: string }>(`${this.api}/api/Auth/refresh`, { refreshToken: token });
  }
  logout(): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(`${this.api}/api/Auth/logout`, {});
  }
  forgotPassword(email: string): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(`${this.api}/api/Auth/forgot-password`, { email });
  }
  resetPassword(email: string, token: string, newPassword: string): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(`${this.api}/api/Auth/reset-password`, { email, token, newPassword });
  }

  // Users
  getCurrentUser(): Observable<User> {
    return this.http.get<User>(`${this.api}/api/Users/me`);
  }
  updateProfile(req: UpdateProfileRequest): Observable<{ message: string }> {
    return this.http.put<{ message: string }>(`${this.api}/api/Users/me`, req);
  }
  getStats(): Observable<UserStats> {
    return this.http.get<UserStats>(`${this.api}/api/Users/stats`);
  }

  // Tasks
  getTasks(status?: string): Observable<TaskItem[]> {
    let params = new HttpParams();
    if (status) params = params.set('status', status);
    return this.http.get<TaskItem[]>(`${this.api}/api/Tasks`, { params });
  }
  createTask(req: CreateTaskRequest): Observable<TaskItem> {
    return this.http.post<TaskItem>(`${this.api}/api/Tasks`, req);
  }
  updateTask(id: number, req: UpdateTaskRequest): Observable<TaskItem> {
    return this.http.patch<TaskItem>(`${this.api}/api/Tasks/${id}`, req);
  }
  deleteTask(id: number): Observable<void> {
    return this.http.delete<void>(`${this.api}/api/Tasks/${id}`);
  }
  completeTask(id: number): Observable<TaskItem> {
    return this.http.post<TaskItem>(`${this.api}/api/Tasks/${id}/complete`, {});
  }

  // Events
  getEvents(from?: string, to?: string): Observable<CalendarEvent[]> {
    let params = new HttpParams();
    if (from) params = params.set('from', from);
    if (to) params = params.set('to', to);
    return this.http.get<CalendarEvent[]>(`${this.api}/api/Events`, { params });
  }
  createEvent(req: CreateEventRequest): Observable<CalendarEvent> {
    return this.http.post<CalendarEvent>(`${this.api}/api/Events`, req);
  }
  updateEvent(id: number, req: Partial<CreateEventRequest>): Observable<CalendarEvent> {
    return this.http.patch<CalendarEvent>(`${this.api}/api/Events/${id}`, req);
  }
  deleteEvent(id: number): Observable<{ message: string }> {
    return this.http.delete<{ message: string }>(`${this.api}/api/Events/${id}`);
  }

  // Messages
  sendMessage(text: string): Observable<SendMessageResponse> {
    return this.http.post<SendMessageResponse>(`${this.api}/api/Messages/send`, { text });
  }
  getChatHistory(limit?: number): Observable<ChatMessage[]> {
    let params = new HttpParams();
    if (limit) params = params.set('limit', limit.toString());
    return this.http.get<ChatMessage[]>(`${this.api}/api/Messages/history`, { params });
  }
  clearChatHistory(): Observable<{ message: string }> {
    return this.http.delete<{ message: string }>(`${this.api}/api/Messages/history`);
  }

  // Google
  getGoogleStatus(): Observable<IntegrationStatus> {
    return this.http.get<IntegrationStatus>(`${this.api}/api/google/status`);
  }
  getGoogleConnectUrl(): Observable<{ url: string }> {
    return this.http.get<{ url: string }>(`${this.api}/api/google/connect-url`);
  }
  disconnectGoogle(): Observable<{ message: string }> {
    return this.http.delete<{ message: string }>(`${this.api}/api/google/disconnect`);
  }

  // Messaging
  getMessagingStatus(): Observable<MessagingStatus> {
    return this.http.get<MessagingStatus>(`${this.api}/api/Messaging/status`);
  }
  initializeTelegram(): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(`${this.api}/api/Messaging/telegram/initialize`, {});
  }
  initializeWhatsApp(): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(`${this.api}/api/Messaging/whatsapp/initialize`, {});
  }
  disconnectTelegram(): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(`${this.api}/api/Messaging/disconnect/telegram`, {});
  }
  disconnectWhatsApp(): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(`${this.api}/api/Messaging/disconnect/whatsapp`, {});
  }
  setMessagingPreference(platform: string): Observable<MessagingPreference> {
    return this.http.post<MessagingPreference>(`${this.api}/api/Messaging/preference`, { platform });
  }
  getMessagingPreference(): Observable<MessagingPreference> {
    return this.http.get<MessagingPreference>(`${this.api}/api/Messaging/preference`);
  }
  registerPlatform(platform: string, platformUserId: string, chatId?: string): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(`${this.api}/api/Messaging/register-platform`, { platform, platformUserId, chatId });
  }
  getUserPlatforms(): Observable<{ platforms: any[]; userId: number }> {
    return this.http.get<{ platforms: any[]; userId: number }>(`${this.api}/api/Messaging/user-platforms`);
  }
  sendTestMessage(message: string): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(`${this.api}/api/Messaging/send-test`, { message });
  }

  // Push Notifications
  registerDeviceToken(token: string, platform: string) {
    return this.http.post(`${this.api}/api/push/register`, { token, platform });
  }
  unregisterDeviceToken(token: string) {
    return this.http.post(`${this.api}/api/push/unregister`, { token });
  }

  // Health
  checkHealth(): Observable<any> {
    return this.http.get(`${this.api}/health/real-time`);
  }

  // Automation
  getAutomationRules(): Observable<AutomationRule[]> {
    return this.http.get<AutomationRule[]>(`${this.api}/api/Automation`);
  }
  createAutomationRule(rule: { name: string; triggerType: string; triggerConfig: string; actionsJson: string }): Observable<AutomationRule> {
    return this.http.post<AutomationRule>(`${this.api}/api/Automation`, rule);
  }
  updateAutomationRule(id: number, data: { name?: string; isActive?: boolean }): Observable<any> {
    return this.http.patch(`${this.api}/api/Automation/${id}`, data);
  }
  deleteAutomationRule(id: number): Observable<any> {
    return this.http.delete(`${this.api}/api/Automation/${id}`);
  }
  getAutomationTemplates(): Observable<AutomationTemplate[]> {
    return this.http.get<AutomationTemplate[]>(`${this.api}/api/Automation/templates`);
  }

  // Documents
  getDocuments(): Observable<DocumentInfo[]> {
    return this.http.get<DocumentInfo[]>(`${this.api}/api/Documents`);
  }
  uploadDocument(file: File): Observable<any> {
    const formData = new FormData();
    formData.append('file', file);
    return this.http.post(`${this.api}/api/Documents/upload`, formData);
  }
  queryDocument(id: number, question: string): Observable<{ answer: string }> {
    return this.http.post<{ answer: string }>(`${this.api}/api/Documents/${id}/query`, { question });
  }
  queryAllDocuments(question: string): Observable<{ answer: string }> {
    return this.http.post<{ answer: string }>(`${this.api}/api/Documents/query-all`, { question });
  }
  deleteDocument(id: number): Observable<any> {
    return this.http.delete(`${this.api}/api/Documents/${id}`);
  }

  // Voice
  transcribeAudio(file: File): Observable<{ text: string }> {
    const formData = new FormData();
    formData.append('audio', file);
    return this.http.post<{ text: string }>(`${this.api}/api/Voice/transcribe`, formData);
  }

  // Gmail
  getGmailStatus(): Observable<{ connected: boolean; provider: string }> {
    return this.http.get<{ connected: boolean; provider: string }>(`${this.api}/api/Gmail/status`);
  }
  getGmailEmails(query?: string, maxResults?: number): Observable<any[]> {
    let params = new HttpParams();
    if (query) params = params.set('query', query);
    if (maxResults) params = params.set('maxResults', maxResults.toString());
    return this.http.get<any[]>(`${this.api}/api/Gmail/emails`, { params });
  }
  getGmailLabels(): Observable<any[]> {
    return this.http.get<any[]>(`${this.api}/api/Gmail/labels`);
  }
  sendGmailEmail(to: string, subject: string, body: string): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(`${this.api}/api/Gmail/send`, { to, subject, body });
  }

  // Calendar
  getCalendarStatus(): Observable<{ connected: boolean; provider: string }> {
    return this.http.get<{ connected: boolean; provider: string }>(`${this.api}/api/Calendar/status`);
  }
  syncCalendar(): Observable<{ message: string }> {
    return this.http.get<{ message: string }>(`${this.api}/api/Calendar/sync`);
  }

  // Trello
  getTrelloStatus(): Observable<{ connected: boolean; configured: boolean; boardId?: string }> {
    return this.http.get<{ connected: boolean; configured: boolean; boardId?: string }>(`${this.api}/api/Trello/status`);
  }
  syncTrelloTasks(): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(`${this.api}/api/Trello/sync`, {});
  }

  // Teams
  getTeams(): Observable<Team[]> {
    return this.http.get<Team[]>(`${this.api}/api/Teams`);
  }
  createTeam(data: { name: string; description?: string }): Observable<Team> {
    return this.http.post<Team>(`${this.api}/api/Teams`, data);
  }
  getTeam(id: number): Observable<Team> {
    return this.http.get<Team>(`${this.api}/api/Teams/${id}`);
  }
  addTeamMember(teamId: number, email: string, role: string): Observable<any> {
    return this.http.post(`${this.api}/api/Teams/${teamId}/members`, { email, role });
  }
  removeTeamMember(teamId: number, userId: number): Observable<any> {
    return this.http.delete(`${this.api}/api/Teams/${teamId}/members/${userId}`);
  }
  deleteTeam(id: number): Observable<any> {
    return this.http.delete(`${this.api}/api/Teams/${id}`);
  }
}
