import { Component, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { NgIf } from '@angular/common';
import { AuthService } from '../../core/services/auth.service';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [FormsModule, RouterLink, NgIf],
  template: `
    <div class="auth-container">
      <a routerLink="/" class="back-home">
        <span class="material-icons">arrow_back</span> Back to Home
      </a>
      <div class="auth-card">
        <div class="auth-header">
          <h1>AI Agent</h1>
          <p>Sign in to your account</p>
        </div>

        <form (ngSubmit)="handleSubmit()" class="auth-form">
          <div class="error-message" *ngIf="error()">{{ error() }}</div>

          <div class="form-group">
            <label for="email">Email</label>
            <input
              type="email"
              id="email"
              [(ngModel)]="email"
              name="email"
              placeholder="Enter your email"
              required
            />
          </div>

          <div class="form-group">
            <label for="password">Password</label>
            <input
              type="password"
              id="password"
              [(ngModel)]="password"
              name="password"
              placeholder="Enter your password"
              required
            />
          </div>

          <div class="form-group forgot-link">
            <a routerLink="/forgot-password">Forgot password?</a>
          </div>

          <button type="submit" class="auth-btn" [disabled]="loading()">
            {{ loading() ? 'Signing in...' : 'Sign in' }}
          </button>
        </form>

        <div class="auth-footer">
          <p>Don't have an account? <a routerLink="/register">Sign up</a></p>
        </div>
      </div>
    </div>
  `,
  styleUrl: './auth.component.scss'
})
export class LoginComponent {
  email = '';
  password = '';
  loading = signal(false);
  error = signal('');

  constructor(
    private auth: AuthService,
    private router: Router
  ) {}

  async handleSubmit(): Promise<void> {
    if (!this.email || !this.password) return;
    this.loading.set(true);
    this.error.set('');

    const success = await this.auth.login({ email: this.email, password: this.password });
    if (success) {
      this.router.navigate(['/dashboard']);
    } else {
      this.error.set('Invalid email or password');
    }
    this.loading.set(false);
  }
}
