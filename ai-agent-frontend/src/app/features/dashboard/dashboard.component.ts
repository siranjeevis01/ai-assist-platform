import { Component, OnInit, signal, computed, effect, OnDestroy } from '@angular/core';
import { NgIf, NgFor, NgClass, DatePipe } from '@angular/common';
import { Router } from '@angular/router';
import { Subscription } from 'rxjs';
import { ApiService } from '../../core/services/api.service';
import { SignalRService } from '../../core/services/signalr.service';
import { ABTestService } from '../../core/services/ab-test.service';
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
              <h3>{{ analytics().tasks.total }}</h3>
              <p>Total Tasks</p>
              <div class="stat-details">
                <span class="completed">{{ analytics().tasks.completed }} done</span>
                <span class="in-progress">{{ analytics().tasks.thisWeek }} this week</span>
              </div>
            </div>
            <div class="stat-bar">
              <div class="stat-bar-fill" [style.width.%]="completionRate()"></div>
            </div>
          </div>

          <div class="stat-card">
            <div class="stat-icon" style="background: linear-gradient(135deg, #43e97b 0%, #38f9d7 100%)">
              <span class="material-icons">calendar_today</span>
            </div>
            <div class="stat-content">
              <h3>{{ analytics().events.total }}</h3>
              <p>Total Events</p>
              <div class="stat-details">
                <span class="upcoming">{{ analytics().events.upcoming }} upcoming</span>
                <span class="this-week">{{ analytics().events.thisWeek }} this week</span>
              </div>
            </div>
            <div class="stat-bar">
              <div class="stat-bar-fill" [style.width.%]="eventActivityRate()"></div>
            </div>
          </div>

          <div class="stat-card">
            <div class="stat-icon" style="background: linear-gradient(135deg, #f5576c 0%, #ff7a5a 100%)">
              <span class="material-icons">smart_toy</span>
            </div>
            <div class="stat-content">
              <h3>{{ analytics().messages.total }}</h3>
              <p>AI Messages</p>
              <div class="stat-details">
                <span class="completed">{{ analytics().messages.thisWeek }} this week</span>
              </div>
            </div>
            <div class="stat-bar">
              <div class="stat-bar-fill" [style.width.%]="100"></div>
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
            <div class="stat-bar">
              <div class="stat-bar-fill" [style.width.%]="completionRate()"></div>
            </div>
          </div>
        </div>

        <div class="stats-row-secondary">
          <div class="mini-stat">
            <span class="material-icons">folder</span>
            <div>
              <strong>{{ analytics().documents.total }}</strong>
              <span>Documents</span>
            </div>
          </div>
          <div class="mini-stat">
            <span class="material-icons">group</span>
            <div>
              <strong>{{ analytics().teams }}</strong>
              <span>Teams</span>
            </div>
          </div>
          <div class="mini-stat">
            <span class="material-icons">bolt</span>
            <div>
              <strong>{{ analytics().automation.active }}</strong>
              <span>Automations</span>
            </div>
          </div>
          <div class="mini-stat">
            <span class="material-icons">run_circle</span>
            <div>
              <strong>{{ analytics().automation.totalRuns }}</strong>
              <span>Auto Runs</span>
            </div>
          </div>
        </div>

        <div class="dashboard-content">
          <div class="content-column">
            <div class="content-card">
              <h3>Recent Tasks</h3>
              <div class="task-list">
                <div *ngIf="recentTasks().length === 0" class="empty-state">
                  <span class="material-icons">checklist</span>
                  <p>No tasks yet</p>
                  <button class="add-btn" (click)="router.navigate(['/tasks'])">Create your first task</button>
                </div>
                <div *ngFor="let task of recentTasks()" class="task-item">
                  <div class="task-checkbox">
                    <input type="checkbox" [checked]="task.status === 'Done'" (change)="handleTaskComplete(task.id)" />
                  </div>
                  <div class="task-content">
                    <div class="task-title" [ngClass]="{'completed': task.status === 'Done'}">{{ task.title }}</div>
                    <div class="task-meta">
                      <span *ngIf="task.dueUtc" class="due-date">Due: {{ task.dueUtc | date:'mediumDate' }}</span>
                      <span class="status-badge" [ngClass]="task.status === 'Done' ? 'done' : task.status === 'In Progress' ? 'in-progress' : 'to-do'">{{ task.status }}</span>
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
                <div *ngIf="todayEvents().length === 0" class="empty-state">
                  <span class="material-icons">event</span>
                  <p>No events today</p>
                  <button class="add-btn" (click)="router.navigate(['/calendar'])">Schedule an event</button>
                </div>
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

        <div class="dashboard-content">
          <div class="content-column full">
            <div class="content-card recent-activity">
              <h3>Recent Activity</h3>
              <div class="activity-list">
                <div *ngIf="recentActivity().length === 0" class="empty-state">No recent activity</div>
                <div *ngFor="let item of recentActivity()" class="activity-item">
                  <div class="activity-icon">
                    <span class="material-icons">{{ item.type === 'task' ? 'checklist' : 'event' }}</span>
                  </div>
                  <div class="activity-content">
                    <span class="activity-title">{{ item.title }}</span>
                    <span class="activity-status" [ngClass]="item.status === 'Done' ? 'done' : 'pending'">{{ item.status }}</span>
                  </div>
                  <span class="activity-time">{{ item.createdAt | date:'short' }}</span>
                </div>
              </div>
            </div>
          </div>
        </div>

        <div class="quick-actions">
          <h3>Quick Actions</h3>
          <div class="action-buttons">
            <button class="action-btn" (click)="router.navigate(['/tasks'])">
              <span class="material-icons">checklist</span> Add Task
            </button>
            <button class="action-btn" (click)="router.navigate(['/calendar'])">
              <span class="material-icons">calendar_today</span> Schedule Event
            </button>
            <button class="action-btn" (click)="router.navigate(['/messages'])">
              <span class="material-icons">chat</span> Chat with AI
            </button>
            <button class="action-btn" (click)="router.navigate(['/documents'])">
              <span class="material-icons">upload_file</span> Upload Doc
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
  analytics = signal<any>({
    tasks: { total: 0, completed: 0, inProgress: 0, toDo: 0, thisWeek: 0, thisMonth: 0 },
    events: { total: 0, upcoming: 0, thisWeek: 0, thisMonth: 0 },
    documents: { total: 0, totalSize: 0 },
    teams: 0,
    automation: { total: 0, active: 0, totalRuns: 0 },
    messages: { total: 0, thisWeek: 0 },
    recentActivity: []
  });
  recentTasks = signal<TaskItem[]>([]);
  todayEvents = signal<CalendarEvent[]>([]);
  recentActivity = signal<any[]>([]);
  googleConnected = signal(false);
  private subs: Subscription[] = [];

  completionRate = computed(() => {
    const a = this.analytics();
    return a.tasks.total > 0 ? Math.round((a.tasks.completed / a.tasks.total) * 100) : 0;
  });

  eventActivityRate = computed(() => {
    const a = this.analytics();
    return a.events.total > 0 ? Math.round((a.events.upcoming / a.events.total) * 100) : 0;
  });

  constructor(
    public router: Router,
    private api: ApiService,
    private signalR: SignalRService,
    private abTest: ABTestService
  ) {
    effect(() => {
      const n = this.signalR.notificationSignal();
      if (n) this.loadDashboardData();
    });
  }

  ngOnInit(): void {
    this.abTest.trackEvent('dashboard_view', 'view');
    this.loadDashboardData();
  }
  ngOnDestroy(): void { this.subs.forEach(s => s.unsubscribe()); }

  loadDashboardData(): void {
    this.subs.push(this.api.getAnalytics().subscribe(data => {
      this.analytics.set(data);
      if (data.recentActivity) this.recentActivity.set(data.recentActivity);
    }));
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
