import { Routes } from '@angular/router';
import { AuthGuard } from './core/guards/auth.guard';
import { LayoutComponent } from './shared/layout/layout.component';

export const routes: Routes = [
  { path: 'login', loadComponent: () => import('./features/auth/login.component').then(m => m.LoginComponent) },
  { path: 'register', loadComponent: () => import('./features/auth/register.component').then(m => m.RegisterComponent) },
  { path: 'forgot-password', loadComponent: () => import('./features/auth/forgot-password.component').then(m => m.ForgotPasswordComponent) },
  { path: 'reset-password', loadComponent: () => import('./features/auth/reset-password.component').then(m => m.ResetPasswordComponent) },
  {
    path: '',
    component: LayoutComponent,
    canActivate: [AuthGuard],
    children: [
      { path: '', redirectTo: 'dashboard', pathMatch: 'full' },
      { path: 'dashboard', loadComponent: () => import('./features/dashboard/dashboard.component').then(m => m.DashboardComponent) },
      { path: 'tasks', loadComponent: () => import('./features/tasks/tasks.component').then(m => m.TasksComponent) },
      { path: 'calendar', loadComponent: () => import('./features/calendar/calendar.component').then(m => m.CalendarComponent) },
      { path: 'messages', loadComponent: () => import('./features/messages/messages.component').then(m => m.MessagesComponent) },
      { path: 'integrations', loadComponent: () => import('./features/integrations/integrations.component').then(m => m.IntegrationsComponent) },
      { path: 'settings', loadComponent: () => import('./features/settings/settings.component').then(m => m.SettingsComponent) },
    ]
  },
  { path: '**', redirectTo: 'dashboard' }
];
