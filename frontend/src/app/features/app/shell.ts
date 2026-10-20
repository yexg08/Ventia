import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  heroArrowRightOnRectangle,
  heroArchiveBox,
  heroBars3,
  heroCog6Tooth,
  heroHome,
  heroTag,
  heroTruck,
  heroUserGroup,
  heroUsers,
  heroXMark,
} from '@ng-icons/heroicons/outline';
import { filter } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { Logo } from '../../shared/logo';
import { ThemeToggle } from '../../shared/theme-toggle';

interface NavItem {
  path: string;
  label: string;
  icon: string;
  exact: boolean;
  ownerOnly: boolean;
}

@Component({
  selector: 'app-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, NgIcon, Logo, ThemeToggle],
  providers: [provideIcons({ heroArrowRightOnRectangle, heroArchiveBox, heroBars3, heroCog6Tooth, heroHome, heroTag, heroTruck, heroUserGroup, heroUsers, heroXMark })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './shell.html',
})
export class Shell {
  protected readonly auth = inject(AuthService);
  protected readonly menuOpen = signal(false);

  // las siguientes fases agregan aqui el punto de venta y los reportes
  private readonly items: NavItem[] = [
    { path: '/app', label: 'Inicio', icon: 'heroHome', exact: true, ownerOnly: false },
    { path: '/app/productos', label: 'Productos', icon: 'heroArchiveBox', exact: false, ownerOnly: false },
    { path: '/app/clientes', label: 'Clientes', icon: 'heroUserGroup', exact: false, ownerOnly: false },
    { path: '/app/proveedores', label: 'Proveedores', icon: 'heroTruck', exact: false, ownerOnly: true },
    { path: '/app/categorias', label: 'Categorías', icon: 'heroTag', exact: false, ownerOnly: false },
    { path: '/app/usuarios', label: 'Usuarios', icon: 'heroUsers', exact: false, ownerOnly: true },
    { path: '/app/configuracion', label: 'Configuración', icon: 'heroCog6Tooth', exact: false, ownerOnly: true },
  ];

  protected readonly nav = computed(() => this.items.filter((item) => !item.ownerOnly || this.auth.isOwner()));
  protected readonly roleLabel = computed(() => (this.auth.isOwner() ? 'Dueño' : 'Cajero'));

  constructor() {
    // en el celular el menu se cierra al navegar
    inject(Router)
      .events.pipe(
        filter((e) => e instanceof NavigationEnd),
        takeUntilDestroyed(),
      )
      .subscribe(() => this.menuOpen.set(false));
  }
}
