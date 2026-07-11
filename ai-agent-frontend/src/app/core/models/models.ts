export interface User {
  id: number;
  email: string;
  name: string;
  role: string;
  timezone: string;
  phoneNumber?: string;
  createdAt: string;
  preference?: Preference;
  integrations?: { google: boolean; trello: boolean };
}

export interface Preference {
  workHours: string;
  defaultDurationMinutes: number;
  defaultBoard: string;
  defaultList: string;
  reminderPolicy: string;
}

export interface AuthResponse {
  token: string;
  refreshToken: string;
  email: string;
  name: string;
}

export interface LoginRequest {
  email: string;
  password: string;
}

export interface RegisterRequest {
  name: string;
  email: string;
  password: string;
}

export interface TaskItem {
  id: number;
  userId: number;
  title: string;
  status: string;
  dueUtc?: string;
  externalId?: string;
  labelsJson?: string;
  description?: string;
  createdAt: string;
  lastReminderSentAt?: string;
  completedAt?: string;
}

export interface CreateTaskRequest {
  title: string;
  dueUtc?: string;
  description?: string;
  labelsCsv?: string;
}

export interface UpdateTaskRequest {
  title?: string;
  status?: string;
  dueUtc?: string;
  description?: string;
  labelsCsv?: string;
}

export interface CalendarEvent {
  id: number;
  userId: number;
  title: string;
  description?: string;
  startUtc: string;
  endUtc: string;
  status: string;
  externalId?: string;
  attendeesJson?: string;
  location?: string;
  source?: string;
}

export interface CreateEventRequest {
  title: string;
  startUtc: string;
  endUtc: string;
  location?: string;
  description?: string;
  attendeesCsv?: string;
}

export interface Message {
  id: number;
  userId: number;
  channel: string;
  direction: string;
  body: string;
  intent?: string;
  entitiesJson?: string;
  correlationId?: string;
  messageType?: string;
  createdAt: string;
}

export interface ChatMessage {
  id: number;
  userId: number;
  role: string;
  text: string;
  createdAt: string;
}

export interface SendMessageResponse {
  result: string;
}

export interface UserStats {
  tasks: { total: number; completed: number; thisWeek: number; thisMonth: number };
  events: { total: number; upcoming: number; thisWeek: number; thisMonth: number };
}

export interface IntegrationStatus {
  connected: boolean;
  expiresAt?: string;
  scopes?: string[];
}

export interface MessagingStatus {
  telegram: { isConnected: boolean; username?: string; lastChecked?: string };
  whatsApp: { isConnected: boolean; status?: string; lastChecked?: string };
}

export interface UpdateProfileRequest {
  name?: string;
  timezone?: string;
  phoneNumber?: string;
  preference?: {
    workHours?: string;
    defaultDurationMinutes?: number;
    defaultBoard?: string;
    defaultList?: string;
    reminderPolicy?: string;
  };
}

export interface SignalRUpdate {
  type: string;
  message: string;
  timestamp: string;
  userId?: string;
}

export interface MessagingPreference {
  platform: string;
  userId: number;
}

export interface AutomationRule {
  id: number;
  userId: number;
  name: string;
  description?: string;
  triggerType: string;
  triggerConfig: string;
  actionsJson: string;
  isActive: boolean;
  runCount: number;
  lastRunAt?: string;
  createdAt: string;
  updatedAt: string;
}

export interface AutomationTemplate {
  name: string;
  description: string;
  triggerType: string;
  triggerConfig: Record<string, string>;
  actions: { type: string; config: Record<string, string>; order: number }[];
}

export interface DocumentInfo {
  id: number;
  fileName: string;
  contentType: string;
  sizeBytes: number;
  summary?: string;
  embeddingStatus: string;
  createdAt: string;
  textPreview?: string;
}

export interface Team {
  id: number;
  name: string;
  description?: string;
  ownerId: number;
  createdAt: string;
  members?: TeamMember[];
}

export interface TeamMember {
  id: number;
  teamId: number;
  userId: number;
  role: string;
  joinedAt: string;
  user?: { id: number; name: string; email: string };
}
