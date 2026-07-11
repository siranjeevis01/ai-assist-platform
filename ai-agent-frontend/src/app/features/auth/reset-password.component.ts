import { Component, OnInit, signal } from '@angular/core';
import { NgIf } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { ApiService } from '../../core/services/api.service';
import { ToastService } from '../../shared/toast/toast.service';

@Component({
  selector: 'app-reset-password',
  standalone: true,
  imports: [NgIf, FormsModule, RouterLink],
  template: `
    <div class="auth-container">
      <a routerLink="/" class="back-home">
        <span class="material-icons">arrow_back</span> Back to Home
      </a>
      <div class="auth-card">
        <div class="auth-header">
          <h1>AI Agent</h1>
          <p>Set a new password</p>
        </div>
        <form (ngSubmit)="handleSubmit()" class="auth-form">
          <div class="form-group">
            <label>New Password</label>
            <input type="password" [(ngModel)]="password" name="password" placeholder="Enter new password" required />
          </div>
          <div class="form-group">
            <label>Confirm Password</label>
            <input type="password" [(ngModel)]="confirmPassword" name="confirm" placeholder="Confirm new password" required />
          </div>
          <div class="error-message" *ngIf="error()">{{ error() }}</div>
          <div class="success-message" *ngIf="reset()">Password reset successfully!</div>
          <button type="submit" class="btn btn-primary btn-block" [disabled]="loading() || !password || password !== confirmPassword">
            {{ loading() ? 'Resetting...' : 'Reset Password' }}
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
export class ResetPasswordComponent implements OnInit {
  password = '';
  confirmPassword = '';
  token = '';
  email = '';
  loading = signal(false);
  error = signal('');
  reset = signal(false);

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private api: ApiService,
    private toast: ToastService
  ) {}

  ngOnInit(): void {
    this.token = this.route.snapshot.queryParams['token'] || '';
    this.email = this.route.snapshot.queryParams['email'] || '';
    if (!this.token || !this.email) {
      this.error.set('Invalid reset link');
    }
  }

  handleSubmit(): void {
    if (!this.password || this.password !== this.confirmPassword) return;
    this.loading.set(true);
    this.error.set('');
    this.api.resetPassword(this.email, this.token, this.password).subscribe({
      next: () => {
        this.reset.set(true);
        this.loading.set(false);
        this.toast.success('Password reset successfully!');
        setTimeout(() => this.router.navigate(['/login']), 2000);
      },
      error: (err) => {
        this.error.set(err.error?.message || 'Failed to reset password');
        this.loading.set(false);
      }
    });
  }
}
