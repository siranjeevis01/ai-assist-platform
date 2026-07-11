import { Component, signal } from '@angular/core';
import { Router, RouterOutlet, RouterLink, RouterLinkActive } from '@angular/router';
import { NgClass, NgFor, NgIf } from '@angular/common';
import { AuthService } from '../../core/services/auth.service';

interface NavItem {
  path: string;
  icon: string;
  label: string;
}

@Component({
  selector: 'app-layout',
  standalone: true,
  imports: [RouterOutlet, RouterLink, RouterLinkActive, NgClass, NgFor, NgIf],
  template: `
    <div class="layout">
      <button class="sidebar-toggle" (click)="sidebarOpen.set(!sidebarOpen())" aria-label="Toggle menu">
        <span class="material-icons">{{ sidebarOpen() ? 'close' : 'menu' }}</span>
      </button>

      <div class="sidebar-overlay" *ngIf="sidebarOpen()" (click)="sidebarOpen.set(false)"></div>

      <div class="sidebar" [ngClass]="{'open': sidebarOpen()}">
        <div class="sidebar-header">
          <h2>AI Agent</h2>
        </div>

        <nav class="sidebar-nav">
          <a *ngFor="let item of navItems"
             [routerLink]="item.path"
             routerLinkActive="active"
             [routerLinkActiveOptions]="{exact: true}"
             class="nav-item"
             (click)="sidebarOpen.set(false)">
            <span class="material-icons">{{ item.icon }}</span>
            <span>{{ item.label }}</span>
          </a>
        </nav>

        <div class="sidebar-footer">
          <div class="user-info">
            <div class="user-avatar">{{ (auth.user()?.name || 'U').charAt(0) }}</div>
            <div class="user-details">
              <div class="user-name">{{ auth.user()?.name || 'User' }}</div>
              <div class="user-email">{{ auth.user()?.email || '' }}</div>
            </div>
          </div>
          <button class="logout-btn" (click)="handleLogout()">
            <span class="material-icons">logout</span>
            <span>Logout</span>
          </button>
        </div>
      </div>

      <div class="main-content">
        <router-outlet></router-outlet>
      </div>
    </div>
  `,
  styleUrl: './layout.component.scss'
})
export class LayoutComponent {
  sidebarOpen = signal(false);

  navItems: NavItem[] = [
    { path: '/dashboard', icon: 'home', label: 'Dashboard' },
    { path: '/tasks', icon: 'checklist', label: 'Tasks' },
    { path: '/calendar', icon: 'calendar_today', label: 'Calendar' },
    { path: '/messages', icon: 'chat', label: 'Messages' },
    { path: '/email', icon: 'email', label: 'Email' },
    { path: '/integrations', icon: 'settings', label: 'Integrations' },
    { path: '/automation', icon: 'bolt', label: 'Automation' },
    { path: '/documents', icon: 'description', label: 'Documents' },
    { path: '/voice', icon: 'mic', label: 'Voice' },
    { path: '/teams', icon: 'group', label: 'Teams' },
    { path: '/settings', icon: 'manage_accounts', label: 'Settings' },
  ];

  constructor(
    public auth: AuthService,
    private router: Router
  ) {}

  handleLogout(): void {
    this.auth.logout();
  }
}
