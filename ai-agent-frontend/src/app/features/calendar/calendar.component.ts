import { Component, OnInit, signal, OnDestroy } from '@angular/core';
import { NgIf, NgFor, DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Subscription } from 'rxjs';
import { ApiService } from '../../core/services/api.service';
import { CalendarEvent } from '../../core/models/models';
import { ToastService } from '../../shared/toast/toast.service';

@Component({
  selector: 'app-calendar',
  standalone: true,
  imports: [NgIf, NgFor, DatePipe, FormsModule],
  template: `
    <div class="calendar-page">
      <div class="page-header">
        <div class="header-content">
          <h1>Calendar</h1>
          <p>Manage your events and schedule</p>
        </div>
        <button class="btn btn-primary" (click)="openNewEvent()">
          <span class="material-icons">add</span> New Event
        </button>
      </div>

      <div class="modal-overlay" *ngIf="showModal()" (click)="closeModal()">
        <div class="modal" (click)="$event.stopPropagation()">
          <h2>{{ editingEvent() ? 'Edit Event' : 'Create New Event' }}</h2>
          <form (ngSubmit)="editingEvent() ? updateEvent() : createEvent()">
            <div class="form-group"><label>Title *</label><input type="text" [(ngModel)]="form.title" name="title" required placeholder="Meeting with team" /></div>
            <div class="form-group"><label>Description</label><textarea [(ngModel)]="form.description" name="desc" rows="3" placeholder="Brief description..."></textarea></div>
            <div class="form-row">
              <div class="form-group"><label>Start *</label><input type="datetime-local" [(ngModel)]="form.startUtc" name="start" required /></div>
              <div class="form-group"><label>End *</label><input type="datetime-local" [(ngModel)]="form.endUtc" name="end" required /></div>
            </div>
            <div class="form-group"><label>Location</label><input type="text" [(ngModel)]="form.location" name="loc" placeholder="Conference Room A" /></div>
            <div class="form-group"><label>Attendees</label><input type="text" [(ngModel)]="form.attendeesCsv" name="att" placeholder="email1@example.com, email2@example.com" /><small>Separate multiple emails with commas</small></div>
            <div class="modal-actions">
              <button type="button" class="btn btn-secondary" (click)="closeModal()">Cancel</button>
              <button type="submit" class="btn btn-primary">{{ editingEvent() ? 'Update' : 'Create' }} Event</button>
            </div>
          </form>
        </div>
      </div>

      <div class="google-sync-bar" *ngIf="calendarConnected() || !calendarConnected()">
        <div class="sync-info">
          <span class="material-icons" style="color: #4285f4">event</span>
          <span>Google Calendar</span>
          <span class="status" [ngClass]="calendarConnected() ? 'connected' : 'disconnected'">
            {{ calendarConnected() ? 'Connected' : 'Not connected' }}
          </span>
        </div>
        <button *ngIf="calendarConnected()" class="btn btn-secondary btn-sm" (click)="syncCalendar()" [disabled]="calendarSyncing()">
          {{ calendarSyncing() ? 'Syncing...' : 'Sync Now' }}
        </button>
      </div>

      <div class="calendar-content">
        <div class="events-section">
          <h2>Upcoming Events ({{ upcomingEvents().length }})</h2>
          <div class="events-list">
            <div *ngIf="loading()" class="loading-state"><div class="spinner"></div><p>Loading events...</p></div>
            <div *ngIf="!loading() && upcomingEvents().length === 0" class="empty-state">
              <span class="material-icons" style="font-size:48px;color:#ccc">calendar_today</span>
              <p>No upcoming events</p>
              <button class="btn btn-primary" (click)="openNewEvent()">Schedule Your First Event</button>
            </div>
            <div *ngFor="let event of upcomingEvents()" class="event-card">
              <div class="event-time">
                <div class="event-date">{{ event.startUtc | date:'mediumDate' }}</div>
                <div class="event-time-display"><span class="material-icons" style="font-size:14px">schedule</span> {{ event.startUtc | date:'shortTime' }} - {{ event.endUtc | date:'shortTime' }}</div>
              </div>
              <div class="event-content">
                <h3>{{ event.title }}</h3>
                <p *ngIf="event.description" class="event-description">{{ event.description }}</p>
                <div class="event-details">
                  <div *ngIf="event.location" class="event-location"><span class="material-icons" style="font-size:14px">location_on</span> {{ event.location }}</div>
                </div>
              </div>
              <div class="event-actions">
                <span class="event-status">{{ event.status || 'Scheduled' }}</span>
                <button class="icon-btn" (click)="openEditEvent(event)" aria-label="Edit event"><span class="material-icons">edit</span></button>
                <button class="icon-btn" (click)="deleteEvent(event.id)" aria-label="Delete event"><span class="material-icons">delete</span></button>
              </div>
            </div>
          </div>
        </div>

        <div *ngIf="pastEvents().length > 0" class="events-section">
          <h2>Past Events ({{ pastEvents().length }})</h2>
          <div class="events-list">
            <div *ngFor="let event of pastEvents()" class="event-card past">
              <div class="event-time">
                <div class="event-date">{{ event.startUtc | date:'mediumDate' }}</div>
                <div class="event-time-display">{{ event.startUtc | date:'shortTime' }} - {{ event.endUtc | date:'shortTime' }}</div>
              </div>
              <div class="event-content">
                <h3>{{ event.title }}</h3>
                <p *ngIf="event.description" class="event-description">{{ event.description }}</p>
                <div *ngIf="event.location" class="event-location"><span class="material-icons" style="font-size:14px">location_on</span> {{ event.location }}</div>
              </div>
              <div class="event-actions"><span class="event-status completed">Completed</span></div>
            </div>
          </div>
        </div>
      </div>
    </div>
  `,
  styleUrl: './calendar.component.scss'
})
export class CalendarComponent implements OnInit, OnDestroy {
  events = signal<CalendarEvent[]>([]);
  loading = signal(true);
  showModal = signal(false);
  editingEvent = signal<CalendarEvent | null>(null);
  calendarConnected = signal(false);
  calendarSyncing = signal(false);

  form = { title: '', description: '', startUtc: '', endUtc: '', location: '', attendeesCsv: '' };
  private subs: Subscription[] = [];

  upcomingEvents = () => this.events().filter(e => new Date(e.startUtc) >= new Date()).sort((a, b) => new Date(a.startUtc).getTime() - new Date(b.startUtc).getTime());
  pastEvents = () => this.events().filter(e => new Date(e.startUtc) < new Date()).sort((a, b) => new Date(b.startUtc).getTime() - new Date(a.startUtc).getTime());

  constructor(
    private api: ApiService,
    private toast: ToastService
  ) {}

  ngOnInit(): void {
    this.loadEvents();
    this.subs.push(this.api.getCalendarStatus().subscribe(s => this.calendarConnected.set(s.connected)));
  }
  ngOnDestroy(): void { this.subs.forEach(s => s.unsubscribe()); }

  loadEvents(): void {
    this.loading.set(true);
    const today = new Date().toISOString();
    const nextMonth = new Date(Date.now() + 30 * 24 * 60 * 60 * 1000).toISOString();
    this.subs.push(this.api.getEvents(today, nextMonth).subscribe(events => {
      this.events.set(events);
      this.loading.set(false);
    }));
  }

  openNewEvent(): void {
    this.editingEvent.set(null);
    this.form = { title: '', description: '', startUtc: '', endUtc: '', location: '', attendeesCsv: '' };
    this.showModal.set(true);
  }

  openEditEvent(event: CalendarEvent): void {
    this.editingEvent.set(event);
    this.form = {
      title: event.title,
      description: event.description || '',
      startUtc: this.toDatetimeLocal(event.startUtc),
      endUtc: this.toDatetimeLocal(event.endUtc),
      location: event.location || '',
      attendeesCsv: ''
    };
    this.showModal.set(true);
  }

  closeModal(): void {
    this.showModal.set(false);
    this.editingEvent.set(null);
  }

  createEvent(): void {
    if (!this.form.title) return;
    this.subs.push(this.api.createEvent(this.form).subscribe(() => {
      this.closeModal();
      this.toast.success('Event created');
      this.loadEvents();
    }));
  }

  updateEvent(): void {
    const event = this.editingEvent();
    if (!event || !this.form.title) return;
    this.subs.push(this.api.updateEvent(event.id, this.form).subscribe(() => {
      this.closeModal();
      this.toast.success('Event updated');
      this.loadEvents();
    }));
  }

  deleteEvent(id: number): void {
    this.subs.push(this.api.deleteEvent(id).subscribe(() => {
      this.toast.success('Event deleted');
      this.loadEvents();
    }));
  }

  syncCalendar(): void {
    this.calendarSyncing.set(true);
    this.subs.push(this.api.syncCalendar().subscribe({
      next: () => { this.calendarSyncing.set(false); this.toast.success('Calendar synced'); this.loadEvents(); },
      error: () => { this.calendarSyncing.set(false); this.toast.error('Failed to sync calendar'); }
    }));
  }

  private toDatetimeLocal(utc: string): string {
    const d = new Date(utc);
    const pad = (n: number) => n.toString().padStart(2, '0');
    return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
  }
}
