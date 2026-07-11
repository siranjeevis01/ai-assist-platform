import { Component, OnInit, signal } from '@angular/core';
import { NgIf, NgFor, DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/services/api.service';
import { GmailEmail } from '../../core/models/models';
import { ToastService } from '../../shared/toast/toast.service';

@Component({
  selector: 'app-email',
  standalone: true,
  imports: [NgIf, NgFor, FormsModule, DatePipe],
  template: `
    <div class="page-container">
      <div class="page-header">
        <div class="header-content">
          <h1>Email</h1>
          <p>Gmail integration</p>
        </div>
        <button class="btn btn-primary" *ngIf="gmailConnected()" (click)="showCompose.set(true)">
          <span class="material-icons">edit</span> Compose
        </button>
      </div>

      <div class="status-bar" *ngIf="!gmailConnected()">
        <div class="status-info">
          <span class="material-icons status-icon disconnected">link_off</span>
          <span>Gmail is not connected</span>
        </div>
        <button class="btn btn-primary" (click)="connectGoogle()">
          <span class="material-icons">link</span> Connect Google
        </button>
      </div>

      <div class="status-bar connected-bar" *ngIf="gmailConnected()">
        <div class="status-info">
          <span class="material-icons status-icon connected">check_circle</span>
          <span>Gmail connected</span>
        </div>
      </div>

      <div class="search-bar" *ngIf="gmailConnected()">
        <span class="material-icons search-icon">search</span>
        <input
          type="text"
          [(ngModel)]="searchQuery"
          placeholder="Search emails..."
          (keyup.enter)="loadEmails()"
        />
        <button class="btn btn-secondary btn-sm" (click)="loadEmails()">
          <span class="material-icons">refresh</span>
        </button>
      </div>

      <div class="email-list" *ngIf="gmailConnected()">
        <div *ngIf="loading()" class="loading-state"><div class="spinner"></div></div>
        <div *ngIf="!loading() && filteredEmails().length === 0" class="empty-state">
          <span class="material-icons" style="font-size:48px;color:#667eea">mail</span>
          <h3>No emails found</h3>
          <p *ngIf="searchQuery">No results for "{{ searchQuery }}"</p>
          <p *ngIf="!searchQuery">Your inbox is empty</p>
        </div>
        <div *ngFor="let email of filteredEmails()" class="email-card" [class.unread]="email.isUnread">
          <div class="email-header">
            <div class="email-from">
              <span class="material-icons avatar-icon">person</span>
              <strong>{{ email.from }}</strong>
              <span class="unread-badge" *ngIf="email.isUnread">NEW</span>
            </div>
            <span class="email-date">{{ email.date | date:'short' }}</span>
          </div>
          <div class="email-subject">{{ email.subject }}</div>
          <div class="email-snippet">{{ email.snippet }}</div>
        </div>
      </div>

      <div class="modal-overlay" *ngIf="showCompose()" (click)="showCompose.set(false)">
        <div class="modal" (click)="$event.stopPropagation()">
          <h2>Compose Email</h2>
          <form (ngSubmit)="sendEmail()">
            <div class="form-group">
              <label>To *</label>
              <input type="email" [(ngModel)]="composeData.to" name="to" required placeholder="recipient@example.com" />
            </div>
            <div class="form-group">
              <label>Subject *</label>
              <input type="text" [(ngModel)]="composeData.subject" name="subject" required placeholder="Email subject" />
            </div>
            <div class="form-group">
              <label>Body *</label>
              <textarea [(ngModel)]="composeData.body" name="body" rows="8" required placeholder="Write your message..."></textarea>
            </div>
            <div class="modal-actions">
              <button type="button" class="btn btn-secondary" (click)="showCompose.set(false)">Cancel</button>
              <button type="submit" class="btn btn-primary" [disabled]="sending()">
                <span class="material-icons" *ngIf="!sending()">send</span>
                <span *ngIf="sending()">Sending...</span>
              </button>
            </div>
          </form>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .status-bar {
      display: flex;
      justify-content: space-between;
      align-items: center;
      background: #2a1a1a;
      border: 1px solid #5c2a2a;
      border-radius: 12px;
      padding: 1rem 1.5rem;
      margin-bottom: 1.5rem;
    }
    .connected-bar {
      background: #1a2a1a;
      border-color: #2a5c2a;
    }
    .status-info {
      display: flex;
      align-items: center;
      gap: 0.75rem;
      color: #e0e0e0;
    }
    .status-icon { font-size: 24px; }
    .status-icon.disconnected { color: #ef5350; }
    .status-icon.connected { color: #66bb6a; }
    .search-bar {
      display: flex;
      align-items: center;
      gap: 0.75rem;
      background: #1e1e2e;
      border: 1px solid #333;
      border-radius: 12px;
      padding: 0.75rem 1rem;
      margin-bottom: 1.5rem;
    }
    .search-icon { color: #888; font-size: 20px; }
    .search-bar input {
      flex: 1;
      background: transparent;
      border: none;
      color: #e0e0e0;
      font-size: 0.95rem;
      outline: none;
    }
    .search-bar input::placeholder { color: #666; }
    .btn-sm { padding: 0.4rem 0.6rem; }
    .email-card {
      background: #1e1e2e;
      border: 1px solid #333;
      border-radius: 12px;
      padding: 1.2rem;
      margin-bottom: 0.75rem;
      cursor: pointer;
      transition: all 0.2s;
    }
    .email-card:hover { border-color: #667eea; }
    .email-card.unread { border-left: 3px solid #667eea; }
    .email-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      margin-bottom: 0.5rem;
    }
    .email-from {
      display: flex;
      align-items: center;
      gap: 0.5rem;
      color: #e0e0e0;
    }
    .avatar-icon { font-size: 20px; color: #667eea; }
    .unread-badge {
      background: #667eea;
      color: white;
      font-size: 0.65rem;
      font-weight: 700;
      padding: 0.15rem 0.4rem;
      border-radius: 4px;
    }
    .email-date { color: #888; font-size: 0.8rem; }
    .email-subject {
      color: #e0e0e0;
      font-weight: 600;
      margin-bottom: 0.3rem;
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }
    .email-snippet {
      color: #888;
      font-size: 0.85rem;
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }
    .modal textarea {
      width: 100%;
      background: #12121c;
      border: 1px solid #333;
      border-radius: 8px;
      color: #e0e0e0;
      padding: 0.75rem;
      font-family: inherit;
      resize: vertical;
    }
    .modal textarea:focus { border-color: #667eea; outline: none; }
    .modal-actions .btn[disabled] { opacity: 0.6; cursor: not-allowed; }
  `]
})
export class EmailComponent implements OnInit {
  emails = signal<GmailEmail[]>([]);
  filteredEmails = signal<GmailEmail[]>([]);
  loading = signal(false);
  gmailConnected = signal(false);
  showCompose = signal(false);
  sending = signal(false);
  searchQuery = '';
  composeData = { to: '', subject: '', body: '' };

  constructor(private api: ApiService, private toast: ToastService) {}

  ngOnInit(): void {
    this.checkStatus();
  }

  checkStatus(): void {
    this.api.getGmailStatus().subscribe({
      next: (status) => {
        this.gmailConnected.set(status.connected);
        if (status.connected) this.loadEmails();
      },
      error: () => this.gmailConnected.set(false)
    });
  }

  connectGoogle(): void {
    this.api.getGoogleConnectUrl().subscribe({
      next: (res) => window.open(res.url, '_blank'),
      error: () => this.toast.error('Failed to get Google connect URL')
    });
  }

  loadEmails(): void {
    this.loading.set(true);
    const query = this.searchQuery || undefined;
    this.api.getGmailEmails(query).subscribe({
      next: (emails) => {
        this.emails.set(emails);
        this.filteredEmails.set(emails);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.toast.error('Failed to load emails');
      }
    });
  }

  sendEmail(): void {
    if (!this.composeData.to || !this.composeData.subject || !this.composeData.body) return;
    this.sending.set(true);
    this.api.sendGmailEmail(this.composeData.to, this.composeData.subject, this.composeData.body).subscribe({
      next: () => {
        this.showCompose.set(false);
        this.composeData = { to: '', subject: '', body: '' };
        this.sending.set(false);
        this.toast.success('Email sent');
      },
      error: () => {
        this.sending.set(false);
        this.toast.error('Failed to send email');
      }
    });
  }
}
