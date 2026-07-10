import { Component, signal } from '@angular/core';
import { NgIf } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ApiService } from '../../core/services/api.service';
import { ToastService } from '../../shared/toast/toast.service';

@Component({
  selector: 'app-forgot-password',
  standalone: true,
  imports: [NgIf, FormsModule, RouterLink],
  template: `
    <div class="auth-page">
      <div class="auth-card">
        <div class="auth-header">
          <h1>AI Agent</h1>
          <p>Reset your password</p>
        </div>
        <form (ngSubmit)="handleSubmit()" class="auth-form">
          <div class="form-group">
            <label>Email Address</label>
            <input type="email" [(ngModel)]="email" name="email" placeholder="you@example.com" required />
          </div>
          <div class="error-message" *ngIf="error()">{{ error() }}</div>
          <div class="success-message" *ngIf="sent()">Check your email for the reset link.</div>
          <button type="submit" class="btn btn-primary btn-block" [disabled]="loading() || !email">
            {{ loading() ? 'Sending...' : 'Send Reset Link' }}
          </button>
          <div class="auth-links">
            <a routerLink="/login">Back to Login</a>
          </div>
        </form>
      </div>
    </div>
  `,
  styleUrl: './auth.component.scss'
})
export class ForgotPasswordComponent {
  email = '';
  loading = signal(false);
  error = signal('');
  sent = signal(false);

  constructor(
    private api: ApiService,
    private toast: ToastService
  ) {}

  handleSubmit(): void {
    if (!this.email) return;
    this.loading.set(true);
    this.error.set('');
    this.api.forgotPassword(this.email).subscribe({
      next: () => {
        this.sent.set(true);
        this.loading.set(false);
        this.toast.success('Reset link sent!');
      },
      error: (err) => {
        this.error.set(err.error?.message || 'Failed to send reset email');
        this.loading.set(false);
      }
    });
  }
}
