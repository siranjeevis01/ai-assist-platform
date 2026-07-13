import { Injectable, signal, OnDestroy, effect } from '@angular/core';
import { HubConnection, HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr';
import { environment } from '../../../environments/environment';
import { AuthService } from './auth.service';
import { ChatMessage, TaskItem, CalendarEvent, SignalRUpdate } from '../models/models';

@Injectable({ providedIn: 'root' })
export class SignalRService implements OnDestroy {
  private hubConnection?: HubConnection;
  private isConnected = false;

  newMessageSignal = signal<ChatMessage | null>(null);
  taskUpdatedSignal = signal<TaskItem | null>(null);
  eventReminderSignal = signal<CalendarEvent | null>(null);
  notificationSignal = signal<SignalRUpdate | null>(null);
  preferenceUpdatedSignal = signal<{ platform: string } | null>(null);
  connectionStatus = signal<'connecting' | 'connected' | 'disconnected' | 'error'>('disconnected');

  constructor(private auth: AuthService) {
    this.tryConnect();
    effect(() => {
      if (this.auth.isAuthenticated() && !this.isConnected) {
        this.startConnection();
      }
      if (!this.auth.isAuthenticated() && this.isConnected) {
        this.stopConnection();
      }
    });
  }

  private tryConnect(): void {
    setTimeout(() => this.startConnection(), 1000);
  }

  async startConnection(): Promise<void> {
    if (this.isConnected) return;
    const token = this.auth.getToken();
    if (!token) { this.connectionStatus.set('disconnected'); return; }

    this.connectionStatus.set('connecting');

    this.hubConnection = new HubConnectionBuilder()
      .withUrl(`${environment.signalRUrl}/hub`, {
        accessTokenFactory: () => this.auth.getToken() ?? ''
      })
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .configureLogging(LogLevel.Warning)
      .build();

    this.hubConnection.onreconnecting(() => {
      this.connectionStatus.set('connecting');
    });

    this.hubConnection.onreconnected(() => {
      this.connectionStatus.set('connected');
      this.subscribeToUserUpdates();
    });

    this.hubConnection.onclose(() => {
      this.isConnected = false;
      this.connectionStatus.set('disconnected');
      setTimeout(() => this.startConnection(), 5000);
    });

    this.registerEvents();

    try {
      await this.hubConnection.start();
      this.isConnected = true;
      this.connectionStatus.set('connected');
      this.subscribeToUserUpdates();
    } catch {
      this.connectionStatus.set('error');
      setTimeout(() => this.startConnection(), 10000);
    }
  }

  private async stopConnection(): Promise<void> {
    if (this.hubConnection) {
      await this.hubConnection.stop();
      this.isConnected = false;
      this.connectionStatus.set('disconnected');
    }
  }

  private registerEvents(): void {
    this.hubConnection?.on('ReceiveUpdate', (data: SignalRUpdate) => {
      this.notificationSignal.set(data);
    });

    this.hubConnection?.on('TaskUpdated', (task: TaskItem) => {
      this.taskUpdatedSignal.set(task);
    });

    this.hubConnection?.on('NewMessage', (message: any) => {
      this.newMessageSignal.set(message);
    });

    this.hubConnection?.on('EventReminder', (event: CalendarEvent) => {
      this.eventReminderSignal.set(event);
    });

    this.hubConnection?.on('ProactiveNotification', (notification: any) => {
      this.notificationSignal.set({
        type: 'ProactiveNotification',
        message: notification.message || notification,
        timestamp: new Date().toISOString()
      });
    });

    this.hubConnection?.on('PreferenceUpdated', (data: { platform: string }) => {
      this.preferenceUpdatedSignal.set(data);
    });

    this.hubConnection?.on('StatsUpdated', () => {});
  }

  private async subscribeToUserUpdates(): Promise<void> {
    if (this.hubConnection?.state === HubConnectionState.Connected) {
      try {
        await this.hubConnection.invoke('SubscribeToUserUpdates');
      } catch {}
    }
  }

  async sendTaskUpdate(task: TaskItem): Promise<void> {
    if (this.hubConnection?.state === HubConnectionState.Connected) {
      await this.hubConnection.invoke('TaskUpdated', task);
    }
  }

  async sendProactiveNotification(notification: any): Promise<void> {
    if (this.hubConnection?.state === HubConnectionState.Connected) {
      await this.hubConnection.invoke('ProactiveNotification', notification);
    }
  }

  ngOnDestroy(): void {
    this.hubConnection?.stop();
  }
}
