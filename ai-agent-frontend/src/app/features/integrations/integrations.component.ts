import { Component, OnInit, signal, OnDestroy } from '@angular/core';
import { NgIf, NgClass } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Subscription } from 'rxjs';
import { ApiService } from '../../core/services/api.service';
import { ToastService } from '../../shared/toast/toast.service';

@Component({
  selector: 'app-integrations',
  standalone: true,
  imports: [NgIf, NgClass, FormsModule],
  template: `
    <div class="integrations-page">
      <div class="page-header">
        <div class="header-content">
          <h1>Integrations</h1>
          <p>Connect your services to the AI Agent</p>
        </div>
        <button class="btn btn-secondary" (click)="loadStatus()">
          <span class="material-icons">refresh</span> Refresh Status
        </button>
      </div>

      <div class="integrations-grid">
        <div class="integration-card">
          <div class="integration-header">
            <div class="integration-icon" style="background: linear-gradient(135deg, #667eea 0%, #764ba2 100%)">
              <span class="material-icons">link</span>
            </div>
            <div class="integration-info">
              <h3>Google</h3>
              <p>Calendar & Gmail integration</p>
            </div>
            <div class="status" [ngClass]="googleConnected() ? 'connected' : 'disconnected'">
              <span class="material-icons">{{ googleConnected() ? 'check_circle' : 'cancel' }}</span>
              {{ googleConnected() ? 'Connected' : 'Disconnected' }}
            </div>
          </div>
          <div class="integration-actions">
            <button *ngIf="!googleConnected()" class="btn btn-primary" (click)="connectGoogle()">
              Connect Google
            </button>
            <button *ngIf="googleConnected()" class="btn btn-secondary" (click)="disconnectGoogle()">
              Disconnect
            </button>
          </div>
        </div>

        <div class="integration-card">
          <div class="integration-header">
            <div class="integration-icon" style="background: linear-gradient(135deg, #43e97b 0%, #38f9d7 100%)">
              <span class="material-icons">message</span>
            </div>
            <div class="integration-info">
              <h3>Telegram</h3>
              <p>Messaging integration</p>
            </div>
            <div class="status" [ngClass]="telegramConnected() ? 'connected' : 'disconnected'">
              <span class="material-icons">{{ telegramConnected() ? 'check_circle' : 'cancel' }}</span>
              {{ telegramConnected() ? 'Connected' : 'Disconnected' }}
            </div>
          </div>
          <div class="integration-actions">
            <button *ngIf="!telegramConnected()" class="btn btn-primary" (click)="initTelegram()" [disabled]="telegramLoading()">
              {{ telegramLoading() ? 'Initializing...' : 'Initialize Telegram' }}
            </button>
            <button *ngIf="telegramConnected()" class="btn btn-secondary" disabled>Connected</button>
          </div>
        </div>

        <div class="integration-card">
          <div class="integration-header">
            <div class="integration-icon" style="background: linear-gradient(135deg, #f5576c 0%, #ff7a5a 100%)">
              <span class="material-icons">chat</span>
            </div>
            <div class="integration-info">
              <h3>WhatsApp</h3>
              <p>Messaging integration</p>
            </div>
            <div class="status" [ngClass]="whatsAppConnected() ? 'connected' : 'disconnected'">
              <span class="material-icons">{{ whatsAppConnected() ? 'check_circle' : 'cancel' }}</span>
              {{ whatsAppConnected() ? 'Connected' : 'Disconnected' }}
            </div>
          </div>
          <div class="integration-actions">
            <button *ngIf="!whatsAppConnected()" class="btn btn-primary" (click)="initWhatsApp()" [disabled]="whatsAppLoading()">
              {{ whatsAppLoading() ? 'Initializing...' : 'Initialize WhatsApp' }}
            </button>
            <button *ngIf="whatsAppConnected()" class="btn btn-secondary" disabled>Connected</button>
          </div>
        </div>

        <div class="integration-card">
          <div class="integration-header">
            <div class="integration-icon" style="background: linear-gradient(135deg, #0079bf 0%, #026aa7 100%)">
              <span class="material-icons">view_kanban</span>
            </div>
            <div class="integration-info">
              <h3>Trello</h3>
              <p>Project board integration</p>
            </div>
            <div class="status" [ngClass]="trelloConnected() ? 'connected' : 'disconnected'">
              <span class="material-icons">{{ trelloConnected() ? 'check_circle' : 'cancel' }}</span>
              {{ trelloConnected() ? 'Connected' : 'Disconnected' }}
            </div>
          </div>
          <div class="integration-actions">
            <button *ngIf="!trelloConnected()" class="btn btn-secondary" disabled>
              Configure in Environment
            </button>
            <button *ngIf="trelloConnected()" class="btn btn-primary" (click)="syncTrello()" [disabled]="trelloLoading()">
              {{ trelloLoading() ? 'Syncing...' : 'Sync Tasks' }}
            </button>
            <button *ngIf="trelloConnected()" class="btn btn-secondary" disabled>Connected</button>
          </div>
        </div>

        <div class="integration-card">
          <div class="integration-header">
            <div class="integration-icon" style="background: linear-gradient(135deg, #4facfe 0%, #00f2fe 100%)">
              <span class="material-icons">email</span>
            </div>
            <div class="integration-info">
              <h3>Messaging Preference</h3>
              <p>Choose your preferred notification channel</p>
            </div>
          </div>
          <div class="integration-actions">
            <div class="preference-select">
              <select [(ngModel)]="selectedPlatform" (change)="setPreference()">
                <option value="telegram">Telegram</option>
                <option value="whatsapp">WhatsApp</option>
              </select>
              <button class="btn btn-primary" (click)="setPreference()">Save Preference</button>
            </div>
          </div>
        </div>
      </div>
    </div>
  `,
  styleUrl: './integrations.component.scss'
})
export class IntegrationsComponent implements OnInit, OnDestroy {
  googleConnected = signal(false);
  telegramConnected = signal(false);
  whatsAppConnected = signal(false);
  trelloConnected = signal(false);
  telegramLoading = signal(false);
  whatsAppLoading = signal(false);
  trelloLoading = signal(false);
  selectedPlatform = 'telegram';
  private subs: Subscription[] = [];

  constructor(
    private api: ApiService,
    private toast: ToastService
  ) {}

  ngOnInit(): void {
    this.loadStatus();
    this.handleOAuthCallback();
  }

  ngOnDestroy(): void { this.subs.forEach(s => s.unsubscribe()); }

  private handleOAuthCallback(): void {
    const params = new URLSearchParams(window.location.search);
    const code = params.get('code');
    const state = params.get('state');
    if (code && state) {
      this.subs.push(this.api.getGoogleStatus().subscribe(() => {
        window.history.replaceState({}, '', window.location.pathname);
        this.loadStatus();
        this.toast.success('Google connected successfully');
      }));
    }
  }

  loadStatus(): void {
    this.subs.push(this.api.getGoogleStatus().subscribe(s => this.googleConnected.set(s.connected)));
    this.subs.push(this.api.getMessagingStatus().subscribe(s => {
      this.telegramConnected.set(s.telegram.isConnected);
      this.whatsAppConnected.set(s.whatsApp.isConnected);
    }));
    this.subs.push(this.api.getMessagingPreference().subscribe(p => {
      if (p.platform) this.selectedPlatform = p.platform;
    }));
    this.subs.push(this.api.getTrelloStatus().subscribe(s => this.trelloConnected.set(s.configured)));
  }

  connectGoogle(): void {
    this.subs.push(this.api.getGoogleConnectUrl().subscribe(res => {
      window.open(res.url, '_blank');
    }));
    const checkFocus = () => {
      if (document.hasFocus()) {
        this.loadStatus();
        window.removeEventListener('focus', checkFocus);
      }
    };
    window.addEventListener('focus', checkFocus);
  }

  disconnectGoogle(): void {
    this.subs.push(this.api.disconnectGoogle().subscribe(() => {
      this.googleConnected.set(false);
      this.toast.success('Google disconnected');
    }));
  }

  initTelegram(): void {
    this.telegramLoading.set(true);
    this.subs.push(this.api.initializeTelegram().subscribe({
      next: () => { this.telegramConnected.set(true); this.telegramLoading.set(false); this.toast.success('Telegram initialized'); },
      error: () => { this.telegramLoading.set(false); this.toast.error('Failed to initialize Telegram'); }
    }));
  }

  initWhatsApp(): void {
    this.whatsAppLoading.set(true);
    this.subs.push(this.api.initializeWhatsApp().subscribe({
      next: () => { this.whatsAppConnected.set(true); this.whatsAppLoading.set(false); this.toast.success('WhatsApp initialized'); },
      error: () => { this.whatsAppLoading.set(false); this.toast.error('Failed to initialize WhatsApp'); }
    }));
  }

  syncTrello(): void {
    this.trelloLoading.set(true);
    this.subs.push(this.api.syncTrelloTasks().subscribe({
      next: () => { this.trelloLoading.set(false); this.toast.success('Tasks synced with Trello'); },
      error: () => { this.trelloLoading.set(false); this.toast.error('Failed to sync with Trello'); }
    }));
  }

  setPreference(): void {
    this.subs.push(this.api.setMessagingPreference(this.selectedPlatform).subscribe(() => {
      this.toast.success('Preference saved');
    }));
  }
}
