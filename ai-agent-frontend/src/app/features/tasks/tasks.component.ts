import { Component, OnInit, signal, computed, OnDestroy } from '@angular/core';
import { NgIf, NgFor, NgClass, DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Subscription } from 'rxjs';
import { ApiService } from '../../core/services/api.service';
import { TaskItem } from '../../core/models/models';
import { ToastService } from '../../shared/toast/toast.service';

@Component({
  selector: 'app-tasks',
  standalone: true,
  imports: [NgIf, NgFor, NgClass, DatePipe, FormsModule],
  template: `
    <div class="tasks-page">
      <div class="page-header">
        <div class="header-content">
          <h1>Tasks</h1>
          <p>Manage your tasks and to-dos</p>
        </div>
        <button class="btn btn-primary" (click)="showNewTask.set(true)">
          <span class="material-icons">add</span> New Task
        </button>
      </div>

      <div class="task-stats">
        <div class="stat-item"><div class="stat-number">{{ tasks().length }}</div><div class="stat-label">Total</div></div>
        <div class="stat-item"><div class="stat-number" style="color:#43e97b">{{ completedCount() }}</div><div class="stat-label">Done</div></div>
        <div class="stat-item"><div class="stat-number" style="color:#ffa726">{{ inProgressCount() }}</div><div class="stat-label">In Progress</div></div>
        <div class="stat-item"><div class="stat-number" style="color:#667eea">{{ todoCount() }}</div><div class="stat-label">To Do</div></div>
      </div>

      <div class="tasks-controls">
        <div class="search-box">
          <span class="material-icons">search</span>
          <input type="text" [(ngModel)]="search" placeholder="Search tasks..." />
        </div>
        <div class="filter-buttons">
          <button *ngFor="let s of statusFilters" class="filter-btn" [ngClass]="{'active': filter() === s}" (click)="filter.set(s); loadTasks()">{{ s }}</button>
        </div>
      </div>

      <div class="modal-overlay" *ngIf="showNewTask()" (click)="showNewTask.set(false)">
        <div class="modal" (click)="$event.stopPropagation()">
          <h2>Create New Task</h2>
          <form (ngSubmit)="createTask()">
            <div class="form-group"><label>Title *</label><input type="text" [(ngModel)]="newTask.title" name="title" required placeholder="What needs to be done?" /></div>
            <div class="form-group"><label>Description</label><textarea [(ngModel)]="newTask.description" name="desc" rows="3" placeholder="Add details..."></textarea></div>
            <div class="form-row">
              <div class="form-group"><label>Due Date</label><input type="datetime-local" [(ngModel)]="newTask.dueUtc" name="due" /></div>
              <div class="form-group"><label>Status</label>
                <select [(ngModel)]="newTask.status" name="status">
                  <option value="To Do">To Do</option>
                  <option value="In Progress">In Progress</option>
                  <option value="Done">Done</option>
                </select>
              </div>
            </div>
            <div class="form-group"><label>Recurrence</label>
              <select [(ngModel)]="recurrenceOption" name="recurrence" (ngModelChange)="onRecurrenceChange($event)">
                <option value="none">None</option>
                <option value="daily">Daily</option>
                <option value="weekly">Weekly</option>
                <option value="biweekly">Biweekly</option>
                <option value="monthly">Monthly</option>
                <option value="yearly">Yearly</option>
              </select>
            </div>
            <div class="modal-actions">
              <button type="button" class="btn btn-secondary" (click)="showNewTask.set(false)">Cancel</button>
              <button type="submit" class="btn btn-primary">Create Task</button>
            </div>
          </form>
        </div>
      </div>

      <div class="tasks-list">
        <div *ngIf="loading()" class="loading-state"><div class="spinner"></div><p>Loading tasks...</p></div>
        <div *ngIf="!loading() && filteredTasks().length === 0" class="empty-state">
          <p>{{ search ? 'No tasks match your search' : 'No tasks found' }}</p>
          <button *ngIf="!search" class="btn btn-primary" (click)="showNewTask.set(true)">Create Your First Task</button>
        </div>
        <div *ngFor="let task of filteredTasks()" class="task-card">
          <div class="task-main">
            <div class="task-checkbox">
              <input type="checkbox" [checked]="task.status === 'Done'" (change)="toggleTaskStatus(task.id, $event)" />
            </div>
            <div class="task-content">
              <h4 [ngClass]="{'completed': task.status === 'Done'}">{{ task.title }}</h4>
              <p *ngIf="task.description" class="task-description">{{ task.description }}</p>
              <div class="task-meta">
                <span *ngIf="task.dueUtc" class="due-date"><span class="material-icons" style="font-size:14px">calendar_today</span> Due: {{ task.dueUtc | date:'mediumDate' }}</span>
                <span *ngIf="task.recurrenceRule" class="due-date"><span class="material-icons" style="font-size:14px">repeat</span> {{ formatRecurrence(task.recurrenceRule) }}</span>
                <span class="status-badge" [class.to-do]="task.status === 'To Do'" [class.in-progress]="task.status === 'In Progress'" [class.done]="task.status === 'Done'">{{ task.status }}</span>
              </div>
            </div>
          </div>
          <div class="task-actions">
            <button class="icon-btn" (click)="deleteTask(task.id)" aria-label="Delete task"><span class="material-icons">delete</span></button>
          </div>
        </div>
      </div>
    </div>
  `,
  styleUrl: './tasks.component.scss'
})
export class TasksComponent implements OnInit, OnDestroy {
  tasks = signal<TaskItem[]>([]);
  filter = signal('all');
  search = '';
  loading = signal(true);
  showNewTask = signal(false);
  private subs: Subscription[] = [];

  statusFilters = ['all', 'To Do', 'In Progress', 'Done'];
  newTask: any = { title: '', description: '', dueUtc: '', status: 'To Do', recurrenceRule: '' };
  recurrenceOption = 'none';

  private readonly recurrenceMap: Record<string, string> = {
    daily: 'FREQ=DAILY',
    weekly: 'FREQ=WEEKLY',
    biweekly: 'FREQ=WEEKLY;INTERVAL=2',
    monthly: 'FREQ=MONTHLY',
    yearly: 'FREQ=YEARLY'
  };

  completedCount = computed(() => this.tasks().filter(t => t.status === 'Done').length);
  inProgressCount = computed(() => this.tasks().filter(t => t.status === 'In Progress').length);
  todoCount = computed(() => this.tasks().filter(t => t.status === 'To Do').length);

  filteredTasks = computed(() => {
    let list = this.filter() === 'all' ? this.tasks() : this.tasks().filter(t => t.status === this.filter());
    if (this.search) {
      const q = this.search.toLowerCase();
      list = list.filter(t => t.title.toLowerCase().includes(q) || t.description?.toLowerCase().includes(q));
    }
    return list;
  });

  constructor(
    private api: ApiService,
    private toast: ToastService
  ) {}

  ngOnInit(): void { this.loadTasks(); }
  ngOnDestroy(): void { this.subs.forEach(s => s.unsubscribe()); }

  loadTasks(): void {
    this.loading.set(true);
    const status = this.filter() === 'all' ? '' : this.filter();
    this.subs.push(this.api.getTasks(status).subscribe({
      next: tasks => { this.tasks.set(tasks); this.loading.set(false); },
      error: () => { this.loading.set(false); this.toast.error('Failed to load tasks'); }
    }));
  }

  createTask(): void {
    if (!this.newTask.title) return;
    this.subs.push(this.api.createTask(this.newTask).subscribe({
      next: () => {
        this.showNewTask.set(false);
        this.newTask = { title: '', description: '', dueUtc: '', status: 'To Do', recurrenceRule: '' };
        this.recurrenceOption = 'none';
        this.toast.success('Task created');
        this.loadTasks();
      },
      error: () => this.toast.error('Failed to create task')
    }));
  }

  onRecurrenceChange(option: string): void {
    this.newTask.recurrenceRule = option === 'none' ? '' : this.recurrenceMap[option] || '';
  }

  formatRecurrence(rule: string): string {
    const entry = Object.entries(this.recurrenceMap).find(([, v]) => rule === v);
    return entry ? entry[0].charAt(0).toUpperCase() + entry[0].slice(1) : rule;
  }

  toggleTaskStatus(id: number, event: any): void {
    const status = event.target.checked ? 'Done' : 'To Do';
    this.subs.push(this.api.updateTask(id, { status }).subscribe({
      next: () => this.loadTasks(),
      error: () => this.toast.error('Failed to update task')
    }));
  }

  deleteTask(id: number): void {
    this.subs.push(this.api.deleteTask(id).subscribe({
      next: () => { this.toast.success('Task deleted'); this.loadTasks(); },
      error: () => this.toast.error('Failed to delete task')
    }));
  }
}
