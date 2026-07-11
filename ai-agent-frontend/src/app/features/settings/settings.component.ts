import { Component, OnInit, effect, signal, OnDestroy } from '@angular/core';
import { NgIf, NgFor } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Subscription } from 'rxjs';
import { ApiService } from '../../core/services/api.service';
import { AuthService } from '../../core/services/auth.service';
import { ToastService } from '../../shared/toast/toast.service';
import { ThemeService, Theme } from '../../core/services/theme.service';

@Component({
  selector: 'app-settings',
  standalone: true,
  imports: [NgIf, NgFor, FormsModule],
  template: `
    <div class="settings-page">
      <div class="page-header">
        <div class="header-content">
          <h1>Settings</h1>
          <p>Manage your account and preferences</p>
        </div>
      </div>

      <div class="settings-grid">
        <div class="settings-card" *ngIf="auth.user()">
          <div class="card-header">
            <span class="material-icons">person</span>
            <h2>Profile</h2>
          </div>
          <form (ngSubmit)="updateProfile()">
            <div class="form-group">
              <label>Name</label>
              <input type="text" [(ngModel)]="profile.name" name="name" placeholder="Your name" />
            </div>
            <div class="form-group">
              <label>Email</label>
              <input type="email" [value]="auth.user()?.email" disabled class="disabled-input" />
            </div>
            <div class="form-group">
              <label>Phone Number</label>
              <input type="tel" [(ngModel)]="profile.phoneNumber" name="phone" placeholder="+1 234 567 890" />
            </div>
            <div class="form-group">
              <label>Timezone</label>
              <select [(ngModel)]="profile.timezone" name="tz" class="responsive-select">
                <option value="UTC">UTC ({{ getTimeForZone('UTC') }})</option>
                <option value="America/New_York">New York ({{ getTimeForZone('America/New_York') }})</option>
                <option value="America/Chicago">Chicago ({{ getTimeForZone('America/Chicago') }})</option>
                <option value="America/Denver">Denver ({{ getTimeForZone('America/Denver') }})</option>
                <option value="America/Los_Angeles">Los Angeles ({{ getTimeForZone('America/Los_Angeles') }})</option>
                <option value="Europe/London">London ({{ getTimeForZone('Europe/London') }})</option>
                <option value="Europe/Berlin">Berlin ({{ getTimeForZone('Europe/Berlin') }})</option>
                <option value="Europe/Paris">Paris ({{ getTimeForZone('Europe/Paris') }})</option>
                <option value="Asia/Tokyo">Tokyo ({{ getTimeForZone('Asia/Tokyo') }})</option>
                <option value="Asia/Shanghai">Shanghai ({{ getTimeForZone('Asia/Shanghai') }})</option>
                <option value="Asia/Kolkata">Kolkata ({{ getTimeForZone('Asia/Kolkata') }})</option>
                <option value="Australia/Sydney">Sydney ({{ getTimeForZone('Australia/Sydney') }})</option>
              </select>
              <small class="field-hint">Current time in your selected timezone updates in real-time</small>
            </div>
            <div class="form-actions">
              <button type="submit" class="btn btn-primary" [disabled]="saving()">
                <span class="material-icons">{{ saving() ? 'hourglass_empty' : 'save' }}</span>
                {{ saving() ? 'Saving...' : 'Save Profile' }}
              </button>
            </div>
          </form>
        </div>

        <div class="settings-card" *ngIf="auth.user()?.preference">
          <div class="card-header">
            <span class="material-icons">task_alt</span>
            <h2>Task Preferences</h2>
          </div>
          <form (ngSubmit)="updatePreferences()">
            <div class="form-group">
              <label>Work Hours</label>
              <input type="text" [(ngModel)]="pref.workHours" name="wh" placeholder="09:00-18:00" />
            </div>
            <div class="form-group">
              <label>Default Task Duration (minutes)</label>
              <input type="number" [(ngModel)]="pref.defaultDurationMinutes" name="dur" min="5" max="480" />
            </div>
            <div class="form-group">
              <label>Reminder Policy</label>
              <select [(ngModel)]="pref.reminderPolicy" name="rem" class="responsive-select">
                <option value="30m-before">30 minutes before</option>
                <option value="1h-before">1 hour before</option>
                <option value="1d-before">1 day before</option>
                <option value="none">No reminders</option>
              </select>
            </div>
            <div class="form-actions">
              <button type="submit" class="btn btn-primary" [disabled]="savingPrefs()">
                <span class="material-icons">{{ savingPrefs() ? 'hourglass_empty' : 'save' }}</span>
                {{ savingPrefs() ? 'Saving...' : 'Save Preferences' }}
              </button>
            </div>
          </form>
        </div>

        <div class="settings-card">
          <div class="card-header">
            <span class="material-icons">notifications</span>
            <h2>Messaging</h2>
          </div>
          <form (ngSubmit)="updateMessagingPref()">
            <div class="form-group">
              <label>Preferred Notification Channel</label>
              <select [(ngModel)]="messagingPlatform" name="msg" class="responsive-select">
                <option value="telegram">Telegram</option>
                <option value="whatsapp">WhatsApp</option>
              </select>
            </div>
            <div class="form-actions">
              <button type="submit" class="btn btn-primary">
                <span class="material-icons">save</span> Save Preference
              </button>
            </div>
          </form>
        </div>

        <div class="settings-card">
          <div class="card-header">
            <span class="material-icons">palette</span>
            <h2>Appearance</h2>
          </div>
          <div class="form-group">
            <label>Theme</label>
            <div class="theme-options">
              <button *ngFor="let t of themes" class="theme-btn" [class.active]="themeService.currentTheme() === t.value" (click)="themeService.setTheme(t.value)">
                <span class="material-icons">{{ t.icon }}</span>
                {{ t.label }}
              </button>
            </div>
          </div>
        </div>

        <div class="settings-card">
          <div class="card-header">
            <span class="material-icons">download</span>
            <h2>Data Export</h2>
          </div>
          <p class="export-desc">Download your data in JSON or CSV format</p>
          <div class="export-actions">
            <button class="btn btn-outline" (click)="exportData('tasks', 'json')">
              <span class="material-icons">task_alt</span> Tasks (JSON)
            </button>
            <button class="btn btn-outline" (click)="exportData('tasks', 'csv')">
              <span class="material-icons">table_chart</span> Tasks (CSV)
            </button>
            <button class="btn btn-outline" (click)="exportData('events', 'json')">
              <span class="material-icons">event</span> Events (JSON)
            </button>
            <button class="btn btn-outline" (click)="exportData('events', 'csv')">
              <span class="material-icons">table_chart</span> Events (CSV)
            </button>
            <button class="btn btn-primary export-all" (click)="exportData('all', 'json')">
              <span class="material-icons">backup</span> Export Everything
            </button>
          </div>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .settings-page { padding: 2rem; }
    .page-header { margin-bottom: 2rem; }
    .header-content h1 { margin: 0; color: #e0e0e0; font-size: 1.8rem; }
    .header-content p { margin: 0.3rem 0 0; color: #888; }
    .settings-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(400px, 1fr)); gap: 1.5rem; }
    .settings-card { background: #1a1a2e; border: 1px solid #2a2a4a; border-radius: 16px; padding: 1.5rem; }
    .card-header { display: flex; align-items: center; gap: 0.6rem; margin-bottom: 1.5rem; }
    .card-header .material-icons { font-size: 24px; color: #667eea; }
    .card-header h2 { margin: 0; color: #e0e0e0; font-size: 1.2rem; }
    .form-group { display: flex; flex-direction: column; gap: 0.4rem; margin-bottom: 1.2rem; }
    .form-group label { font-weight: 500; color: #aaa; font-size: 0.85rem; }
    .form-group input, .form-group select {
      padding: 0.75rem 1rem;
      background: #2a2a3e;
      border: 1px solid #333;
      border-radius: 10px;
      color: #e0e0e0;
      font-size: 0.95rem;
      transition: border-color 0.2s;
      width: 100%;
    }
    .form-group input:focus, .form-group select:focus { border-color: #667eea; outline: none; }
    .form-group input::placeholder { color: #666; }
    .disabled-input { opacity: 0.5; cursor: not-allowed; }
    .responsive-select {
      appearance: none;
      background-image: url("data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='12' height='12' viewBox='0 0 12 12'%3E%3Cpath fill='%23888' d='M6 8L1 3h10z'/%3E%3C/svg%3E");
      background-repeat: no-repeat;
      background-position: right 12px center;
      padding-right: 36px !important;
      cursor: pointer;
    }
    .field-hint { font-size: 0.75rem; color: #666; margin-top: 0.3rem; }
    .form-actions { margin-top: 0.5rem; }
    .btn { display: inline-flex; align-items: center; gap: 0.4rem; padding: 0.7rem 1.4rem; border: none; border-radius: 10px; font-size: 0.9rem; font-weight: 600; cursor: pointer; transition: all 0.2s; }
    .btn .material-icons { font-size: 18px; }
    .btn-primary { background: linear-gradient(135deg, #667eea, #764ba2); color: #fff; }
    .btn-primary:hover:not(:disabled) { transform: translateY(-1px); box-shadow: 0 4px 15px rgba(102,126,234,0.4); }
    .btn:disabled { opacity: 0.5; cursor: not-allowed; }
    @media (max-width: 768px) {
      .settings-grid { grid-template-columns: 1fr; }
    }
    .theme-options { display: flex; gap: 8px; flex-wrap: wrap; }
    .theme-btn {
      display: flex; align-items: center; gap: 6px; padding: 8px 16px;
      background: #2a2a3e; border: 2px solid #333; border-radius: 10px;
      color: #aaa; cursor: pointer; font-size: 14px; transition: all 0.2s;
      &:hover { border-color: #667eea; color: #e0e0e0; }
      &.active { border-color: #667eea; background: rgba(102,126,234,0.15); color: #e0e0e0; }
      .material-icons { font-size: 18px; }
    }
    .export-desc { color: #888; font-size: 14px; margin-bottom: 16px; }
    .export-actions { display: flex; flex-wrap: wrap; gap: 8px; }
    .btn-outline {
      background: transparent; border: 1px solid #444; color: #aaa;
      &:hover { border-color: #667eea; color: #e0e0e0; }
    }
    .export-all { width: 100%; justify-content: center; }
  `]
})
export class SettingsComponent implements OnInit, OnDestroy {
  saving = signal(false);
  savingPrefs = signal(false);
  saved = signal(false);
  currentTime = signal('');
  private inited = false;
  private subs: Subscription[] = [];
  private timeInterval: any;

  profile = { name: '', phoneNumber: '', timezone: '' };
  pref = { workHours: '09:00-18:00', defaultDurationMinutes: 30, defaultBoard: 'default', defaultList: 'To Do', reminderPolicy: '30m-before' };
  messagingPlatform = 'telegram';

  themes = [
    { value: 'light' as Theme, label: 'Light', icon: 'light_mode' },
    { value: 'dark' as Theme, label: 'Dark', icon: 'dark_mode' },
    { value: 'system' as Theme, label: 'System', icon: 'computer' }
  ];

  constructor(
    private api: ApiService,
    public auth: AuthService,
    private toast: ToastService,
    public themeService: ThemeService
  ) {
    effect(() => {
      if (this.inited) return;
      const u = this.auth.user();
      if (u) {
        this.inited = true;
        this.profile = { name: u.name || '', phoneNumber: u.phoneNumber || '', timezone: u.timezone || Intl.DateTimeFormat().resolvedOptions().timeZone };
        if (u.preference) {
          this.pref = { ...u.preference };
        }
      }
    });
  }

  ngOnInit(): void {
    this.subs.push(this.api.getMessagingPreference().subscribe(p => {
      if (p.platform) this.messagingPlatform = p.platform;
    }));
    this.updateClock();
    this.timeInterval = setInterval(() => this.updateClock(), 1000);
  }

  ngOnDestroy(): void {
    this.subs.forEach(s => s.unsubscribe());
    clearInterval(this.timeInterval);
  }

  private updateClock(): void {
    this.currentTime.set(new Date().toLocaleTimeString());
  }

  getTimeForZone(tz: string): string {
    try {
      return new Date().toLocaleTimeString('en-US', { timeZone: tz, hour: '2-digit', minute: '2-digit', hour12: true });
    } catch {
      return '--:--';
    }
  }

  updateProfile(): void {
    this.saving.set(true);
    this.subs.push(this.api.updateProfile(this.profile).subscribe({
      next: () => { this.saving.set(false); this.toast.success('Profile updated'); },
      error: () => { this.saving.set(false); this.toast.error('Failed to update profile'); }
    }));
  }

  updatePreferences(): void {
    this.savingPrefs.set(true);
    this.subs.push(this.api.updateProfile({ preference: this.pref }).subscribe({
      next: () => { this.savingPrefs.set(false); this.toast.success('Preferences saved'); },
      error: () => { this.savingPrefs.set(false); this.toast.error('Failed to save preferences'); }
    }));
  }

  updateMessagingPref(): void {
    this.subs.push(this.api.setMessagingPreference(this.messagingPlatform).subscribe({
      next: () => this.toast.success('Messaging preference saved'),
      error: () => this.toast.error('Failed to save preference')
    }));
  }

  exportData(type: string, format: string): void {
    const obs = type === 'all' ? this.api.exportAll(format) :
                 type === 'tasks' ? this.api.exportTasks(format) :
                 this.api.exportEvents(format);
    this.subs.push(obs.subscribe({
      next: (blob) => {
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = `${type}_export_${new Date().toISOString().split('T')[0]}.${format}`;
        a.click();
        URL.revokeObjectURL(url);
        this.toast.success(`${type} exported successfully`);
      },
      error: () => this.toast.error(`Failed to export ${type}`)
    }));
  }
}
