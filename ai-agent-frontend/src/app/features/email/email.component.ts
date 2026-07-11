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
    <div class="email-page">
      <div class="page-header">
        <div class="header-content">
          <h1>Email</h1>
          <p>Manage your Gmail inbox</p>
        </div>
        <div class="header-actions" *ngIf="gmailConnected()">
          <button class="btn btn-secondary" (click)="loadEmails()">
            <span class="material-icons">refresh</span> Refresh
          </button>
          <button class="btn btn-primary" (click)="showCompose.set(true)">
            <span class="material-icons">edit</span> Compose
          </button>
        </div>
      </div>

      <div class="connect-banner" *ngIf="!gmailConnected()">
        <div class="banner-icon">
          <span class="material-icons">email</span>
        </div>
        <div class="banner-info">
          <h3>Connect your Gmail</h3>
          <p>Read, compose, and manage emails directly from the AI Agent</p>
        </div>
        <button class="btn btn-primary" (click)="connectGoogle()">
          <span class="material-icons">link</span> Connect Google
        </button>
      </div>

      <div class="email-layout" *ngIf="gmailConnected()">
        <div class="email-sidebar">
          <div class="sidebar-section">
            <h4>Inbox</h4>
            <div class="folder active">
              <span class="material-icons">inbox</span> Primary
              <span class="count" *ngIf="emails().length">{{ emails().length }}</span>
            </div>
          </div>
        </div>

        <div class="email-main">
          <div class="search-bar">
            <span class="material-icons search-icon">search</span>
            <input type="text" [(ngModel)]="searchQuery" placeholder="Search emails..." (keyup.enter)="loadEmails()" />
            <button class="icon-btn" (click)="loadEmails()" title="Refresh">
              <span class="material-icons">refresh</span>
            </button>
          </div>

          <div class="email-list">
            <div *ngIf="loading()" class="loading-state"><div class="spinner"></div></div>
            <div *ngIf="!loading() && filteredEmails().length === 0" class="empty-state">
              <span class="material-icons" style="font-size:64px;color:#333">mail</span>
              <h3>No emails found</h3>
              <p *ngIf="searchQuery">No results for "{{ searchQuery }}"</p>
              <p *ngIf="!searchQuery">Your inbox is empty</p>
            </div>
            <div *ngFor="let email of filteredEmails()" class="email-row" [class.unread]="email.isUnread" (click)="selectEmail(email)">
              <div class="email-avatar">{{ (email.from || 'U').charAt(0).toUpperCase() }}</div>
              <div class="email-content">
                <div class="email-top">
                  <span class="email-from" [class.bold]="email.isUnread">{{ email.from }}</span>
                  <span class="email-date">{{ email.date | date:'short' }}</span>
                </div>
                <div class="email-subject" [class.bold]="email.isUnread">{{ email.subject }}</div>
                <div class="email-snippet">{{ email.snippet }}</div>
              </div>
              <span class="unread-dot" *ngIf="email.isUnread"></span>
            </div>
          </div>
        </div>
      </div>

      <div class="email-detail-overlay" *ngIf="selectedEmail()" (click)="selectedEmail.set(null)">
        <div class="email-detail" (click)="$event.stopPropagation()">
          <div class="detail-header">
            <div class="detail-from">
              <div class="detail-avatar">{{ (selectedEmail()!.from || 'U').charAt(0).toUpperCase() }}</div>
              <div>
                <div class="detail-name">{{ selectedEmail()!.from }}</div>
                <div class="detail-date">{{ selectedEmail()!.date | date:'full' }}</div>
              </div>
            </div>
            <button class="icon-btn" (click)="selectedEmail.set(null)">
              <span class="material-icons">close</span>
            </button>
          </div>
          <div class="detail-subject">{{ selectedEmail()!.subject }}</div>
          <div class="detail-body">{{ selectedEmail()!.snippet }}</div>
        </div>
      </div>

      <div class="compose-overlay" *ngIf="showCompose()" (click)="showCompose.set(false)">
        <div class="compose-modal" (click)="$event.stopPropagation()">
          <div class="compose-header">
            <h2>New Message</h2>
            <button class="icon-btn" (click)="showCompose.set(false)">
              <span class="material-icons">close</span>
            </button>
          </div>
          <form (ngSubmit)="sendEmail()">
            <div class="compose-field">
              <label>To</label>
              <input type="email" [(ngModel)]="composeData.to" name="to" required placeholder="recipient@example.com" />
            </div>
            <div class="compose-field">
              <label>Subject</label>
              <input type="text" [(ngModel)]="composeData.subject" name="subject" required placeholder="Email subject" />
            </div>
            <div class="compose-field">
              <textarea [(ngModel)]="composeData.body" name="body" rows="12" required placeholder="Write your message..."></textarea>
            </div>
            <div class="compose-actions">
              <button type="button" class="btn btn-secondary" (click)="showCompose.set(false)">Discard</button>
              <button type="submit" class="btn btn-primary" [disabled]="sending()">
                <span class="material-icons">{{ sending() ? 'hourglass_empty' : 'send' }}</span>
                {{ sending() ? 'Sending...' : 'Send' }}
              </button>
            </div>
          </form>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .email-page { padding: 2rem; height: 100%; }
    .page-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 2rem; }
    .header-content h1 { margin: 0; color: #e0e0e0; font-size: 1.8rem; }
    .header-content p { margin: 0.3rem 0 0; color: #888; }
    .header-actions { display: flex; gap: 0.8rem; }
    .connect-banner { display: flex; align-items: center; gap: 1.5rem; background: #1a1a2e; border: 1px solid #2a2a4a; border-radius: 16px; padding: 2rem; margin-bottom: 2rem; }
    .banner-icon .material-icons { font-size: 48px; color: #667eea; }
    .banner-info { flex: 1; }
    .banner-info h3 { margin: 0; color: #e0e0e0; }
    .banner-info p { margin: 0.3rem 0 0; color: #888; font-size: 0.9rem; }
    .email-layout { display: grid; grid-template-columns: 220px 1fr; gap: 0; background: #1a1a2e; border: 1px solid #2a2a4a; border-radius: 16px; overflow: hidden; min-height: 600px; }
    .email-sidebar { border-right: 1px solid #2a2a4a; padding: 1rem 0; }
    .sidebar-section h4 { padding: 0.5rem 1.2rem; color: #666; font-size: 0.75rem; text-transform: uppercase; letter-spacing: 1px; margin: 0; }
    .folder { display: flex; align-items: center; gap: 0.6rem; padding: 0.6rem 1.2rem; color: #aaa; cursor: pointer; transition: all 0.2s; font-size: 0.9rem; }
    .folder:hover { background: rgba(102,126,234,0.1); color: #e0e0e0; }
    .folder.active { background: rgba(102,126,234,0.15); color: #667eea; border-right: 3px solid #667eea; }
    .folder .material-icons { font-size: 20px; }
    .folder .count { margin-left: auto; background: #667eea; color: white; font-size: 0.7rem; padding: 0.1rem 0.5rem; border-radius: 10px; font-weight: 600; }
    .email-main { display: flex; flex-direction: column; }
    .search-bar { display: flex; align-items: center; gap: 0.5rem; padding: 0.8rem 1rem; border-bottom: 1px solid #2a2a4a; }
    .search-icon { color: #666; font-size: 20px; }
    .search-bar input { flex: 1; background: transparent; border: none; color: #e0e0e0; font-size: 0.95rem; outline: none; }
    .search-bar input::placeholder { color: #666; }
    .icon-btn { width: 36px; height: 36px; border: none; border-radius: 8px; background: transparent; color: #888; cursor: pointer; display: flex; align-items: center; justify-content: center; transition: all 0.2s; }
    .icon-btn:hover { background: #2a2a3e; color: #e0e0e0; }
    .email-list { flex: 1; overflow-y: auto; }
    .email-row { display: flex; align-items: flex-start; gap: 1rem; padding: 1rem 1.2rem; border-bottom: 1px solid #1e1e2e; cursor: pointer; transition: background 0.15s; position: relative; }
    .email-row:hover { background: rgba(102,126,234,0.05); }
    .email-row.unread { background: rgba(102,126,234,0.08); }
    .email-avatar { width: 40px; height: 40px; border-radius: 50%; background: linear-gradient(135deg, #667eea, #764ba2); color: white; display: flex; align-items: center; justify-content: center; font-weight: 600; font-size: 0.9rem; flex-shrink: 0; }
    .email-content { flex: 1; min-width: 0; }
    .email-top { display: flex; justify-content: space-between; align-items: center; margin-bottom: 0.2rem; }
    .email-from { color: #ccc; font-size: 0.9rem; }
    .email-from.bold, .email-subject.bold { font-weight: 700; color: #e0e0e0; }
    .email-date { color: #666; font-size: 0.75rem; flex-shrink: 0; }
    .email-subject { color: #bbb; font-size: 0.9rem; margin-bottom: 0.15rem; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
    .email-snippet { color: #666; font-size: 0.8rem; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
    .unread-dot { width: 8px; height: 8px; border-radius: 50%; background: #667eea; flex-shrink: 0; margin-top: 0.4rem; }
    .email-detail-overlay { position: fixed; inset: 0; background: rgba(0,0,0,0.6); display: flex; align-items: center; justify-content: center; z-index: 1000; padding: 2rem; }
    .email-detail { background: #1a1a2e; border: 1px solid #2a2a4a; border-radius: 16px; width: 100%; max-width: 700px; max-height: 80vh; overflow-y: auto; padding: 2rem; }
    .detail-header { display: flex; justify-content: space-between; align-items: flex-start; margin-bottom: 1.5rem; }
    .detail-from { display: flex; gap: 1rem; align-items: center; }
    .detail-avatar { width: 48px; height: 48px; border-radius: 50%; background: linear-gradient(135deg, #667eea, #764ba2); color: white; display: flex; align-items: center; justify-content: center; font-weight: 600; font-size: 1.1rem; }
    .detail-name { color: #e0e0e0; font-weight: 600; }
    .detail-date { color: #888; font-size: 0.8rem; }
    .detail-subject { color: #e0e0e0; font-size: 1.3rem; font-weight: 700; margin-bottom: 1.5rem; }
    .detail-body { color: #bbb; line-height: 1.8; white-space: pre-wrap; }
    .compose-overlay { position: fixed; inset: 0; background: rgba(0,0,0,0.6); display: flex; align-items: center; justify-content: center; z-index: 1000; padding: 2rem; }
    .compose-modal { background: #1a1a2e; border: 1px solid #2a2a4a; border-radius: 16px; width: 100%; max-width: 600px; }
    .compose-header { display: flex; justify-content: space-between; align-items: center; padding: 1.2rem 1.5rem; border-bottom: 1px solid #2a2a4a; }
    .compose-header h2 { margin: 0; color: #e0e0e0; font-size: 1.1rem; }
    .compose-field { padding: 0 1.5rem; }
    .compose-field:first-of-type { padding-top: 1.2rem; }
    .compose-field label { display: block; color: #888; font-size: 0.8rem; padding: 0.5rem 0 0.3rem; }
    .compose-field input, .compose-field textarea { width: 100%; background: transparent; border: none; border-bottom: 1px solid #2a2a4a; color: #e0e0e0; padding: 0.6rem 0; font-size: 0.95rem; outline: none; font-family: inherit; resize: none; }
    .compose-field input:focus, .compose-field textarea:focus { border-color: #667eea; }
    .compose-field textarea { border: 1px solid #2a2a4a; border-radius: 8px; margin: 0.5rem 0; padding: 0.8rem; min-height: 200px; }
    .compose-actions { display: flex; gap: 0.8rem; justify-content: flex-end; padding: 1rem 1.5rem; border-top: 1px solid #2a2a4a; }
    .btn { display: inline-flex; align-items: center; gap: 0.4rem; padding: 0.6rem 1.2rem; border: none; border-radius: 8px; font-size: 0.9rem; font-weight: 600; cursor: pointer; transition: all 0.2s; }
    .btn .material-icons { font-size: 18px; }
    .btn-primary { background: linear-gradient(135deg, #667eea, #764ba2); color: #fff; }
    .btn-primary:hover:not(:disabled) { transform: translateY(-1px); box-shadow: 0 4px 15px rgba(102,126,234,0.4); }
    .btn-secondary { background: #2a2a3e; color: #e0e0e0; border: 1px solid #444; }
    .btn:disabled { opacity: 0.5; cursor: not-allowed; }
    @media (max-width: 768px) {
      .email-layout { grid-template-columns: 1fr; }
      .email-sidebar { display: none; }
      .page-header { flex-direction: column; gap: 1rem; align-items: flex-start; }
    }
  `]
})
export class EmailComponent implements OnInit {
  emails = signal<GmailEmail[]>([]);
  filteredEmails = signal<GmailEmail[]>([]);
  loading = signal(false);
  gmailConnected = signal(false);
  showCompose = signal(false);
  sending = signal(false);
  selectedEmail = signal<GmailEmail | null>(null);
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
      next: (res) => window.location.href = res.url,
      error: () => this.toast.error('Failed to get Google connect URL')
    });
  }

  loadEmails(): void {
    this.loading.set(true);
    const query = this.searchQuery || undefined;
    this.api.getGmailEmails(query).subscribe({
      next: (emails) => { this.emails.set(emails); this.filteredEmails.set(emails); this.loading.set(false); },
      error: () => { this.loading.set(false); this.toast.error('Failed to load emails'); }
    });
  }

  selectEmail(email: GmailEmail): void {
    this.selectedEmail.set(email);
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
      error: () => { this.sending.set(false); this.toast.error('Failed to send email'); }
    });
  }
}
