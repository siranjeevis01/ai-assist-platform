import { Component, OnInit, signal, computed, effect, OnDestroy } from '@angular/core';
import { NgIf, NgFor, NgClass, DatePipe } from '@angular/common';
import { Router } from '@angular/router';
import { Subscription } from 'rxjs';
import { ApiService } from '../../core/services/api.service';
import { SignalRService } from '../../core/services/signalr.service';
import { UserStats, TaskItem, CalendarEvent } from '../../core/models/models';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [NgIf, NgFor, NgClass, DatePipe],
  template: `
    <div class="dashboard">
      <div class="dashboard-header">
        <h1>Dashboard</h1>
        <p>Welcome back! Here's your productivity overview.</p>
      </div>

      <div *ngIf="loading()" class="loading-state">
        <div class="spinner"></div>
        <p>Loading dashboard...</p>
      </div>

      <ng-container *ngIf="!loading()">
        <div class="stats-grid">
          <div class="stat-card">
            <div class="stat-icon" style="background: linear-gradient(135deg, #667eea 0%, #764ba2 100%)">
              <span class="material-icons">checklist</span>
            </div>
            <div class="stat-content">
              <h3>{{ stats().tasks.total }}</h3>
              <p>Total Tasks</p>
              <div class="stat-details">
                <span class="completed">{{ stats().tasks.completed }} done</span>
                <span class="in-progress">{{ stats().tasks.thisWeek }} this week</span>
              </div>
            </div>
          </div>

          <div class="stat-card">
            <div class="stat-icon" style="background: linear-gradient(135deg, #43e97b 0%, #38f9d7 100%)">
              <span class="material-icons">calendar_today</span>
            </div>
            <div class="stat-content">
              <h3>{{ stats().events.total }}</h3>
              <p>Total Events</p>
              <div class="stat-details">
                <span class="upcoming">{{ stats().events.upcoming }} upcoming</span>
                <span class="this-week">{{ stats().events.thisWeek }} this week</span>
              </div>
            </div>
          </div>

          <div class="stat-card">
            <div class="stat-icon" style="background: linear-gradient(135deg, #f5576c 0%, #ff7a5a 100%)">
              <span class="material-icons">mail</span>
            </div>
            <div class="stat-content">
              <h3 *ngIf="googleConnected()">&#10003;</h3>
              <h3 *ngIf="!googleConnected()">&#10007;</h3>
              <p>Gmail Connected</p>
              <div class="stat-details">
                <span class="synced" *ngIf="googleConnected()">Connected</span>
                <span class="disconnected" *ngIf="!googleConnected()">Not Connected</span>
              </div>
            </div>
          </div>

          <div class="stat-card">
            <div class="stat-icon" style="background: linear-gradient(135deg, #4facfe 0%, #00f2fe 100%)">
              <span class="material-icons">trending_up</span>
            </div>
            <div class="stat-content">
              <h3>{{ completionRate() }}%</h3>
              <p>Completion Rate</p>
              <div class="stat-details">
                <span class="positive">Keep going!</span>
              </div>
            </div>
          </div>
        </div>

        <div class="dashboard-content">
          <div class="content-column">
            <div class="content-card">
              <h3>Recent Tasks</h3>
              <div class="task-list">
                <div *ngIf="recentTasks().length === 0" class="empty-state">No tasks found</div>
                <div *ngFor="let task of recentTasks()" class="task-item">
                  <div class="task-checkbox">
                    <input type="checkbox" [checked]="task.status === 'Done'" (change)="handleTaskComplete(task.id)" />
                  </div>
                  <div class="task-content">
                    <div class="task-title" [ngClass]="{'completed': task.status === 'Done'}">{{ task.title }}</div>
                    <div class="task-meta">
                      <span *ngIf="task.dueUtc" class="due-date">Due: {{ task.dueUtc | date:'mediumDate' }}</span>
                      <span class="status-badge to-do">{{ task.status }}</span>
                    </div>
                  </div>
                </div>
              </div>
            </div>
          </div>

          <div class="content-column">
            <div class="content-card">
              <h3>Today's Events</h3>
              <div class="event-list">
                <div *ngIf="todayEvents().length === 0" class="empty-state">No events today</div>
                <div *ngFor="let event of todayEvents()" class="event-item">
                  <div class="event-time">{{ event.startUtc | date:'shortTime' }}</div>
                  <div class="event-content">
                    <div class="event-title">{{ event.title }}</div>
                    <div *ngIf="event.location" class="event-location">{{ event.location }}</div>
                  </div>
                </div>
              </div>
            </div>
          </div>
        </div>

        <div class="quick-actions">
          <h3>Quick Actions</h3>
          <div class="action-buttons">
            <button class="action-btn" (click)="router.navigate(['/tasks'])" aria-label="Add task">
              <span class="material-icons">checklist</span> Add Task
            </button>
            <button class="action-btn" (click)="router.navigate(['/calendar'])" aria-label="Schedule event">
              <span class="material-icons">calendar_today</span> Schedule Event
            </button>
            <button class="action-btn" (click)="router.navigate(['/messages'])" aria-label="Chat with AI">
              <span class="material-icons">chat</span> Chat with AI
            </button>
          </div>
        </div>
      </ng-container>
    </div>
  `,
  styleUrl: './dashboard.component.scss'
})
export class DashboardComponent implements OnInit, OnDestroy {
  loading = signal(true);
  stats = signal<UserStats>({ tasks: { total: 0, completed: 0, thisWeek: 0, thisMonth: 0 }, events: { total: 0, upcoming: 0, thisWeek: 0, thisMonth: 0 } });
  recentTasks = signal<TaskItem[]>([]);
  todayEvents = signal<CalendarEvent[]>([]);
  googleConnected = signal(false);
  private subs: Subscription[] = [];

  completionRate = computed(() => {
    const s = this.stats();
    return s.tasks.total > 0 ? Math.round((s.tasks.completed / s.tasks.total) * 100) : 0;
  });

  constructor(
    public router: Router,
    private api: ApiService,
    private signalR: SignalRService
  ) {
    effect(() => {
      const n = this.signalR.notificationSignal();
      if (n) this.loadDashboardData();
    });
  }

  ngOnInit(): void { this.loadDashboardData(); }
  ngOnDestroy(): void { this.subs.forEach(s => s.unsubscribe()); }

  loadDashboardData(): void {
    this.subs.push(this.api.getStats().subscribe(stats => this.stats.set(stats)));
    this.subs.push(this.api.getTasks().subscribe(tasks => this.recentTasks.set(tasks.slice(0, 5))));
    this.subs.push(this.api.getEvents(new Date().toISOString()).subscribe(events => {
      const today = new Date().toDateString();
      this.todayEvents.set(events.filter(e => e.startUtc && new Date(e.startUtc).toDateString() === today).slice(0, 5));
    }));
    this.subs.push(this.api.getGoogleStatus().subscribe(s => this.googleConnected.set(s.connected)));
    this.loading.set(false);
  }

  handleTaskComplete(taskId: number): void {
    this.subs.push(this.api.completeTask(taskId).subscribe(() => this.loadDashboardData()));
  }
}
