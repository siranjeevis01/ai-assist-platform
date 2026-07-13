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
              <span class="material-icons">link</span> Connect Google
            </button>
            <button *ngIf="googleConnected()" class="btn btn-danger" (click)="disconnectGoogle()">
              <span class="material-icons">link_off</span> Disconnect
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
              <span class="material-icons">{{ telegramLoading() ? 'hourglass_empty' : 'power' }}</span>
              {{ telegramLoading() ? 'Initializing...' : 'Initialize Telegram' }}
            </button>
            <button *ngIf="telegramConnected()" class="btn btn-danger" (click)="disconnectTelegram()" [disabled]="telegramLoading()">
              <span class="material-icons">link_off</span> Disconnect
            </button>
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
              <span class="material-icons">{{ whatsAppLoading() ? 'hourglass_empty' : 'power' }}</span>
              {{ whatsAppLoading() ? 'Initializing...' : 'Initialize WhatsApp' }}
            </button>
            <button *ngIf="whatsAppConnected()" class="btn btn-danger" (click)="disconnectWhatsApp()" [disabled]="whatsAppLoading()">
              <span class="material-icons">link_off</span> Disconnect
            </button>
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
              <span class="material-icons">info</span> Configure via Environment
            </button>
            <button *ngIf="trelloConnected()" class="btn btn-primary" (click)="syncTrello()" [disabled]="trelloLoading()">
              <span class="material-icons">{{ trelloLoading() ? 'hourglass_empty' : 'sync' }}</span>
              {{ trelloLoading() ? 'Syncing...' : 'Sync Tasks' }}
            </button>
          </div>
        </div>

        <div class="integration-card full-width">
          <div class="integration-header">
            <div class="integration-icon" style="background: linear-gradient(135deg, #4facfe 0%, #00f2fe 100%)">
              <span class="material-icons">notifications</span>
            </div>
            <div class="integration-info">
              <h3>Messaging Preference</h3>
              <p>Choose your preferred notification channel</p>
            </div>
          </div>
          <div class="integration-actions">
            <div class="preference-select">
              <select [(ngModel)]="selectedPlatform" (change)="setPreference()" class="responsive-select">
                <option value="telegram">Telegram</option>
                <option value="whatsapp">WhatsApp</option>
              </select>
              <button class="btn btn-primary" (click)="setPreference()">
                <span class="material-icons">save</span> Save Preference
              </button>
            </div>
          </div>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .integrations-page { padding: 2rem; }
    .page-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 2rem; }
    .header-content h1 { margin: 0; color: var(--text-primary); font-size: 1.8rem; }
    .header-content p { margin: 0.3rem 0 0; color: var(--text-secondary); }
    .integrations-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(380px, 1fr)); gap: 1.5rem; }
    .integration-card { background: var(--bg-card); border: 1px solid var(--border-color); border-radius: 16px; padding: 1.5rem; transition: all 0.3s; }
    .integration-card:hover { border-color: var(--primary-color); transform: translateY(-2px); box-shadow: 0 8px 25px rgba(0,0,0,0.3); }
    .integration-card.full-width { grid-column: 1 / -1; }
    .integration-header { display: flex; align-items: center; gap: 1rem; margin-bottom: 1.2rem; }
    .integration-icon { width: 48px; height: 48px; border-radius: 12px; display: flex; align-items: center; justify-content: center; flex-shrink: 0; }
    .integration-icon .material-icons { font-size: 24px; color: white; }
    .integration-info { flex: 1; }
    .integration-info h3 { margin: 0; color: var(--text-primary); font-size: 1.1rem; }
    .integration-info p { margin: 0.2rem 0 0; color: var(--text-secondary); font-size: 0.85rem; }
    .status { display: flex; align-items: center; gap: 0.4rem; padding: 0.3rem 0.8rem; border-radius: 20px; font-size: 0.8rem; font-weight: 600; }
    .status.connected { background: rgba(67, 233, 123, 0.1); color: #43e97b; }
    .status.disconnected { background: rgba(245, 87, 108, 0.1); color: #f5576c; }
    .status .material-icons { font-size: 16px; }
    .integration-actions { display: flex; gap: 0.8rem; flex-wrap: wrap; }
    .preference-select { display: flex; gap: 0.8rem; align-items: center; width: 100%; }
    .responsive-select { flex: 1; max-width: 300px; padding: 0.6rem 1rem; background: var(--bg-hover); border: 1px solid var(--border-color); border-radius: 8px; color: var(--text-primary); font-size: 0.95rem; cursor: pointer; }
    .responsive-select:focus { border-color: var(--primary-color); outline: none; }
    .btn { display: inline-flex; align-items: center; gap: 0.4rem; padding: 0.6rem 1.2rem; border: none; border-radius: 8px; font-size: 0.9rem; font-weight: 600; cursor: pointer; transition: all 0.2s; }
    .btn .material-icons { font-size: 18px; }
    .btn-primary { background: linear-gradient(135deg, #667eea, #764ba2); color: white; }
    .btn-primary:hover { transform: translateY(-1px); box-shadow: 0 4px 15px rgba(102,126,234,0.4); }
    .btn-secondary { background: var(--bg-hover); color: var(--text-primary); border: 1px solid var(--border-color); }
    .btn-danger { background: rgba(245, 87, 108, 0.15); color: #f5576c; border: 1px solid rgba(245, 87, 108, 0.3); }
    .btn-danger:hover { background: rgba(245, 87, 108, 0.25); }
    .btn:disabled { opacity: 0.5; cursor: not-allowed; transform: none; }
    @media (max-width: 768px) {
      .integrations-grid { grid-template-columns: 1fr; }
      .page-header { flex-direction: column; gap: 1rem; align-items: flex-start; }
      .preference-select { flex-direction: column; }
      .responsive-select { max-width: 100%; }
    }
  `]
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
      window.location.href = res.url;
    }));
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

  disconnectTelegram(): void {
    this.telegramLoading.set(true);
    this.subs.push(this.api.disconnectTelegram().subscribe({
      next: () => { this.telegramConnected.set(false); this.telegramLoading.set(false); this.toast.success('Telegram disconnected'); },
      error: () => { this.telegramLoading.set(false); this.toast.error('Failed to disconnect Telegram'); }
    }));
  }

  initWhatsApp(): void {
    this.whatsAppLoading.set(true);
    this.subs.push(this.api.initializeWhatsApp().subscribe({
      next: () => { this.whatsAppConnected.set(true); this.whatsAppLoading.set(false); this.toast.success('WhatsApp initialized'); },
      error: () => { this.whatsAppLoading.set(false); this.toast.error('Failed to initialize WhatsApp'); }
    }));
  }

  disconnectWhatsApp(): void {
    this.whatsAppLoading.set(true);
    this.subs.push(this.api.disconnectWhatsApp().subscribe({
      next: () => { this.whatsAppConnected.set(false); this.whatsAppLoading.set(false); this.toast.success('WhatsApp disconnected'); },
      error: () => { this.whatsAppLoading.set(false); this.toast.error('Failed to disconnect WhatsApp'); }
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
