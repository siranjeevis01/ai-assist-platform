import { Component, OnInit, effect, signal, OnDestroy } from '@angular/core';
import { NgIf } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Subscription } from 'rxjs';
import { ApiService } from '../../core/services/api.service';
import { AuthService } from '../../core/services/auth.service';
import { ToastService } from '../../shared/toast/toast.service';

@Component({
  selector: 'app-settings',
  standalone: true,
  imports: [NgIf, FormsModule],
  template: `
    <div class="settings-page">
      <div class="page-header">
        <div class="header-content">
          <h1>Settings</h1>
          <p>Manage your preferences</p>
        </div>
      </div>

      <div class="settings-card" *ngIf="auth.user()">
        <h2>Profile Information</h2>
        <form (ngSubmit)="updateProfile()">
          <div class="form-group">
            <label>Name</label>
            <input type="text" [(ngModel)]="profile.name" name="name" />
          </div>
          <div class="form-group">
            <label>Email</label>
            <input type="email" [value]="auth.user()?.email" disabled />
          </div>
          <div class="form-group">
            <label>Phone Number</label>
            <input type="tel" [(ngModel)]="profile.phoneNumber" name="phone" />
          </div>
          <div class="form-group">
            <label>Timezone</label>
            <select [(ngModel)]="profile.timezone" name="tz">
              <option value="UTC">UTC</option>
              <option value="America/New_York">America/New_York</option>
              <option value="America/Chicago">America/Chicago</option>
              <option value="America/Denver">America/Denver</option>
              <option value="America/Los_Angeles">America/Los_Angeles</option>
              <option value="Europe/London">Europe/London</option>
              <option value="Europe/Berlin">Europe/Berlin</option>
              <option value="Europe/Paris">Europe/Paris</option>
              <option value="Asia/Tokyo">Asia/Tokyo</option>
              <option value="Asia/Shanghai">Asia/Shanghai</option>
              <option value="Asia/Kolkata">Asia/Kolkata</option>
              <option value="Australia/Sydney">Australia/Sydney</option>
            </select>
          </div>
          <div class="form-actions">
            <button type="submit" class="btn btn-primary" [disabled]="saving()">{{ saving() ? 'Saving...' : 'Save Changes' }}</button>
          </div>
        </form>
      </div>

      <div class="settings-card" *ngIf="auth.user()?.preference">
        <h2>Task Preferences</h2>
        <form (ngSubmit)="updatePreferences()">
          <div class="form-group">
            <label>Work Hours</label>
            <input type="text" [(ngModel)]="pref.workHours" name="wh" placeholder="09:00-18:00" />
          </div>
          <div class="form-group">
            <label>Default Task Duration (minutes)</label>
            <input type="number" [(ngModel)]="pref.defaultDurationMinutes" name="dur" />
          </div>
          <div class="form-group">
            <label>Reminder Policy</label>
            <select [(ngModel)]="pref.reminderPolicy" name="rem">
              <option value="30m-before">30 minutes before</option>
              <option value="1h-before">1 hour before</option>
              <option value="1d-before">1 day before</option>
              <option value="none">No reminders</option>
            </select>
          </div>
          <div class="form-actions">
            <button type="submit" class="btn btn-primary" [disabled]="savingPrefs()">{{ savingPrefs() ? 'Saving...' : 'Save Preferences' }}</button>
          </div>
        </form>
        <div class="success-message" *ngIf="saved()">Preferences saved successfully!</div>
      </div>

      <div class="settings-card">
        <h2>Messaging Preferences</h2>
        <form (ngSubmit)="updateMessagingPref()">
          <div class="form-group">
            <label>Preferred Notification Channel</label>
            <select [(ngModel)]="messagingPlatform" name="msg">
              <option value="telegram">Telegram</option>
              <option value="whatsapp">WhatsApp</option>
            </select>
          </div>
          <div class="form-actions">
            <button type="submit" class="btn btn-primary">Save Preference</button>
          </div>
        </form>
      </div>
    </div>
  `,
  styleUrl: './settings.component.scss'
})
export class SettingsComponent implements OnInit, OnDestroy {
  saving = signal(false);
  savingPrefs = signal(false);
  saved = signal(false);
  private inited = false;
  private subs: Subscription[] = [];

  profile = { name: '', phoneNumber: '', timezone: '' };
  pref = { workHours: '09:00-18:00', defaultDurationMinutes: 30, defaultBoard: 'default', defaultList: 'To Do', reminderPolicy: '30m-before' };
  messagingPlatform = 'telegram';

  constructor(
    private api: ApiService,
    public auth: AuthService,
    private toast: ToastService
  ) {
    effect(() => {
      if (this.inited) return;
      const u = this.auth.user();
      if (u) {
        this.inited = true;
        this.profile = { name: u.name || '', phoneNumber: u.phoneNumber || '', timezone: u.timezone || 'UTC' };
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
  }

  ngOnDestroy(): void { this.subs.forEach(s => s.unsubscribe()); }

  updateProfile(): void {
    this.saving.set(true);
    this.subs.push(this.api.updateProfile(this.profile).subscribe(() => {
      this.saving.set(false);
      this.saved.set(true);
      this.toast.success('Profile updated');
      setTimeout(() => this.saved.set(false), 3000);
    }));
  }

  updatePreferences(): void {
    this.savingPrefs.set(true);
    this.subs.push(this.api.updateProfile({ preference: this.pref }).subscribe(() => {
      this.savingPrefs.set(false);
      this.saved.set(true);
      this.toast.success('Preferences saved');
      setTimeout(() => this.saved.set(false), 3000);
    }));
  }

  updateMessagingPref(): void {
    this.subs.push(this.api.setMessagingPreference(this.messagingPlatform).subscribe(() => {
      this.toast.success('Messaging preference saved');
    }));
  }
}
