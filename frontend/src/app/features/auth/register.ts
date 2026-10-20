import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { heroEye, heroEyeSlash } from '@ng-icons/heroicons/outline';
import { AuthService } from '../../core/auth/auth.service';
import { getErrorMessage, getFieldErrors } from '../../core/http/api';
import { AuthLayout } from './auth-layout';

const PASSWORD_RULE = /^(?=.*[A-Za-zÀ-ÿ])(?=.*\d).{10,}$/;

type Field = 'businessName' | 'fullName' | 'email' | 'password';

@Component({
  selector: 'app-register',
  imports: [ReactiveFormsModule, RouterLink, NgIcon, AuthLayout],
  providers: [provideIcons({ heroEye, heroEyeSlash })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './register.html',
})
export class Register {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly form = inject(NonNullableFormBuilder).group({
    businessName: ['', [Validators.required, Validators.maxLength(80)]],
    fullName: ['', [Validators.required, Validators.maxLength(80)]],
    email: ['', [Validators.required, Validators.email]],
    password: ['', [Validators.required, Validators.pattern(PASSWORD_RULE)]],
  });

  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly serverErrors = signal<Record<string, string>>({});
  protected readonly showPassword = signal(false);

  constructor() {
    // un error del servidor deja de aplicar en cuanto el usuario cambia algo
    this.form.valueChanges.pipe(takeUntilDestroyed(inject(DestroyRef))).subscribe(() => {
      if (Object.keys(this.serverErrors()).length > 0) this.serverErrors.set({});
    });
  }

  protected invalid(control: Field): boolean {
    const c = this.form.controls[control];
    return (c.invalid && c.touched) || !!this.serverErrors()[control];
  }

  protected submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.loading.set(true);
    this.error.set(null);
    this.serverErrors.set({});
    const raw = this.form.getRawValue();
    this.auth.register({ ...raw, businessName: raw.businessName.trim(), fullName: raw.fullName.trim(), email: raw.email.trim() }).subscribe({
      next: () => void this.router.navigateByUrl('/app'),
      error: (e: unknown) => {
        const fields = getFieldErrors(e);
        this.serverErrors.set(fields);
        if (Object.keys(fields).length === 0) this.error.set(getErrorMessage(e));
        this.loading.set(false);
      },
    });
  }
}
