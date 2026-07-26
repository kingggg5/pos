import { Component, inject, signal, computed, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { PosApiService } from './core/api/pos-api.service';
import { AuthService } from './core/services/auth.service';
import { I18nService } from './core/i18n/i18n.service';
import {
	AuditLog,
	CashShift,
	CartItem,
	Category,
	Customer,
	CreateStaffRequest,
	Order,
	OrderItem,
	OrderQuote,
	PaymentMethod,
	PlatformDashboard,
	Product,
	Promotion,
	PromotionDiscountType,
	StaffUser,
	StoreSettings,
	ZReportSummary
} from './core/models/pos.models';

type NavTab =
	| 'pos'
	| 'products'
	| 'orders'
	| 'reports'
	| 'customers'
	| 'promotions'
	| 'shifts'
	| 'staff'
	| 'audit'
	| 'admin';
type OrderAction = 'Void' | 'Refund';

@Component({
	selector: 'app-root',
	standalone: true,
	imports: [CommonModule, FormsModule],
	templateUrl: './app.html',
	styleUrl: './app.scss'
})
export class App implements OnInit {
	protected readonly api = inject(PosApiService);
	protected readonly auth = inject(AuthService);
	protected readonly i18n = inject(I18nService);

	protected readonly activeTab = signal<NavTab>('pos');

	protected readonly loginEmail = signal('cashier@coffee.com');
	protected readonly loginPassword = signal('password123');
	protected readonly authError = signal('');
	protected readonly isRegisterMode = signal(false);
	protected readonly regStoreName = signal('My New Cafe');
	protected readonly regStoreSlug = signal('my-new-cafe');
	protected readonly regOwnerEmail = signal('owner@mynewcafe.com');
	protected readonly regOwnerPassword = signal('password123');
	protected readonly regOwnerName = signal('Cafe Owner');

	protected readonly categories = signal<Category[]>([]);
	protected readonly products = signal<Product[]>([]);
	protected readonly selectedCategoryId = signal<number>(0);
	protected readonly searchQuery = signal('');
	protected readonly isLoadingProducts = signal(false);
	protected readonly productsError = signal('');

	protected readonly cart = signal<CartItem[]>([]);
	protected readonly discountAmount = signal<number>(0);
	protected readonly paidAmount = signal<number>(0);
	protected readonly selectedPaymentMethod = signal<PaymentMethod>('Cash');

	protected readonly storeSettings = signal<StoreSettings>({
		id: 0,
		name: '',
		slug: '',
		qrCodeUrl: null,
		vatRate: 7,
		serviceChargeRate: 0,
		receiptHeaderNote: 'Thank you for visiting!',
		receiptFooterNote: 'Tax Invoice / Receipt',
		businessTimeZoneId: 'Asia/Bangkok'
	});
	protected readonly isStoreSettingsModalOpen = signal(false);
	protected readonly settingsQrCodeUrl = signal<string | null>(null);
	protected readonly settingsVatRate = signal<number>(7);
	protected readonly settingsServiceChargeRate = signal<number>(0);
	protected readonly settingsReceiptHeader = signal<string>('Thank you for visiting!');
	protected readonly settingsReceiptFooter = signal<string>('Tax Invoice / Receipt');
	protected readonly settingsBusinessTimeZoneId = signal<string>('Asia/Bangkok');

	protected readonly activeStaffUser = signal<StaffUser | null>(null);
	protected readonly isSwitchStaffModalOpen = signal(false);
	protected readonly isActionMenuOpen = signal(false);
	protected readonly isAdminPage = signal(false);

	protected readonly cartSubTotalRaw = computed(() =>
		this.cart().reduce((sum, item) => sum + item.subTotal, 0)
	);
	protected readonly cartSubTotalAfterDiscount = computed(() => {
		const quote = this.checkoutQuote();
		return quote
			? Math.max(0, quote.subTotalAmount - quote.totalDiscountAmount)
			: Math.max(0, this.cartSubTotalRaw() - this.discountAmount());
	});
	protected readonly cartServiceChargeAmount = computed(() => {
		const quote = this.checkoutQuote();
		return quote
			? quote.serviceChargeAmount
			: Math.max(
				0,
				this.cartSubTotalAfterDiscount() * (this.storeSettings().serviceChargeRate / 100)
			);
	});
	protected readonly cartVatAmount = computed(() => {
		const quote = this.checkoutQuote();
		return quote
			? quote.vatAmount
			: Math.max(
				0,
				(this.cartSubTotalAfterDiscount() + this.cartServiceChargeAmount()) *
					(this.storeSettings().vatRate / 100)
			);
	});
	protected readonly cartFinalTotal = computed(() => {
		const quote = this.checkoutQuote();
		return quote
			? quote.totalAmount
			: Math.max(
				0,
				this.cartSubTotalAfterDiscount() +
					this.cartServiceChargeAmount() +
					this.cartVatAmount()
			);
	});
	protected readonly changeAmount = computed(() =>
		Math.max(0, this.paidAmount() - this.cartFinalTotal())
	);

	protected readonly isCheckoutModalOpen = signal(false);
	protected readonly isSlipModalOpen = signal(false);
	protected readonly completedOrder = signal<Order | null>(null);
	protected readonly isProcessingPayment = signal(false);
	protected readonly checkoutError = signal('');
	protected readonly checkoutIdempotencyKey = signal('');
	protected readonly isProductModalOpen = signal(false);
	protected readonly isDeleteConfirmOpen = signal(false);
	protected readonly deletingProductId = signal(0);
	protected readonly editingProductId = signal(0);
	protected readonly prodCategoryId = signal(1);
	protected readonly prodBarcode = signal('');
	protected readonly prodName = signal('');
	protected readonly prodPrice = signal(50);
	protected readonly prodCost = signal(20);
	protected readonly prodStock = signal(50);
	protected readonly prodImageUrl = signal('');

	protected readonly staffUsers = signal<StaffUser[]>([]);
	protected readonly isStaffModalOpen = signal(false);
	protected readonly editingStaffId = signal(0);
	protected readonly staffEmail = signal('');
	protected readonly staffPassword = signal('password123');
	protected readonly staffFullName = signal('');
	protected readonly staffEmployeeCode = signal('');
	protected readonly staffPositionTitle = signal('');
	protected readonly staffRole = signal<'Owner' | 'Manager' | 'Cashier'>('Cashier');
	protected readonly staffCanCheckout = signal(true);
	protected readonly staffCanProducts = signal(false);
	protected readonly staffCanReports = signal(false);
	protected readonly staffCanUsers = signal(false);

	protected readonly orders = signal<Order[]>([]);
	protected readonly isLoadingOrders = signal(false);
	protected readonly ordersError = signal('');
	protected readonly zReport = signal<ZReportSummary | null>(null);
	protected readonly isLoadingZReport = signal(false);
	protected readonly zReportError = signal('');
	protected readonly auditLogs = signal<AuditLog[]>([]);
	protected readonly platformDashboard = signal<PlatformDashboard | null>(null);

	protected readonly currentCashShift = signal<CashShift | null>(null);
	protected readonly cashShifts = signal<CashShift[]>([]);
	protected readonly isLoadingCashShift = signal(false);
	protected readonly shiftError = signal('');
	protected readonly shiftSuccess = signal('');
	protected readonly isShiftModalOpen = signal(false);
	protected readonly shiftModalMode = signal<'Open' | 'Close'>('Open');
	protected readonly openingCash = signal(0);
	protected readonly shiftOpeningNote = signal('');
	protected readonly countedCash = signal(0);
	protected readonly shiftNotes = signal('');
	protected readonly isSavingShift = signal(false);
	protected readonly shiftIdempotencyKey = signal('');

	protected readonly customers = signal<Customer[]>([]);
	protected readonly customerSearchPhone = signal('');
	protected readonly customerSearchResults = signal<Customer[]>([]);
	protected readonly isSearchingCustomers = signal(false);
	protected readonly customerError = signal('');
	protected readonly selectedCustomer = signal<Customer | null>(null);
	protected readonly isCustomerModalOpen = signal(false);
	protected readonly newCustomerName = signal('');
	protected readonly newCustomerPhone = signal('');
	protected readonly newCustomerEmail = signal('');
	protected readonly isSavingCustomer = signal(false);

	protected readonly promotions = signal<Promotion[]>([]);
	protected readonly isLoadingPromotions = signal(false);
	protected readonly promotionError = signal('');
	protected readonly isPromotionModalOpen = signal(false);
	protected readonly editingPromotionId = signal(0);
	protected readonly promoCode = signal('');
	protected readonly promoName = signal('');
	protected readonly promoDiscountType = signal<PromotionDiscountType>('Percentage');
	protected readonly promoDiscountValue = signal(10);
	protected readonly promoMinimumSpend = signal(0);
	protected readonly promoMaximumDiscount = signal<number | null>(null);
	protected readonly promoStartsAt = signal('');
	protected readonly promoEndsAt = signal('');
	protected readonly promoUsageLimit = signal<number | null>(null);
	protected readonly promoIsActive = signal(true);
	protected readonly isSavingPromotion = signal(false);

	protected readonly couponCode = signal('');
	protected readonly appliedCouponCode = signal<string | null>(null);
	protected readonly redeemPoints = signal(0);
	protected readonly checkoutQuote = signal<OrderQuote | null>(null);
	protected readonly isLoadingCheckoutQuote = signal(false);
	protected readonly quoteError = signal('');

	protected readonly selectedOrder = signal<Order | null>(null);
	protected readonly orderAction = signal<OrderAction>('Refund');
	protected readonly isOrderActionModalOpen = signal(false);
	protected readonly orderActionReason = signal('');
	protected readonly isProcessingOrderAction = signal(false);
	protected readonly orderActionError = signal('');
	protected readonly orderActionSuccess = signal('');
	protected readonly orderActionIdempotencyKey = signal('');
	protected readonly refundQuantities = signal<Record<number, number>>({});

	protected readonly canManageOrderActions = computed(() =>
		this.auth.currentUser()?.role === 'Owner' || this.auth.currentUser()?.role === 'Manager'
	);
	protected readonly canManageStore = computed(() =>
		this.auth.currentUser()?.role === 'Owner' || this.auth.currentUser()?.role === 'Manager'
	);
	protected readonly canManageStaff = computed(() =>
		this.auth.currentUser()?.role === 'Owner'
	);
	protected readonly hasOpenCashShift = computed(() =>
		this.currentCashShift()?.status === 'Open'
	);
	protected readonly quoteDiscountTotal = computed(() => {
		const quote = this.checkoutQuote();
		return quote ? quote.totalDiscountAmount : 0;
	});
	protected readonly selectedRefundItems = computed(() => {
		const order = this.selectedOrder();
		if (!order || this.orderAction() !== 'Refund') return [];
		const selected = this.refundQuantities();
		return order.items
			.map((item) => ({
				orderItemId: item.id,
				quantity: this.clampRefundQuantity(
					selected[item.id] ?? 0,
					this.getRefundableQuantity(item)
				)
			}))
			.filter((item) => item.quantity > 0);
	});
	protected readonly selectedRefundQuantity = computed(() =>
		this.selectedRefundItems().reduce((sum, item) => sum + item.quantity, 0)
	);
	protected readonly estimatedRefundAmount = computed(() => {
		const order = this.selectedOrder();
		if (!order || order.subTotalAmount === undefined || order.subTotalAmount <= 0) return 0;
		const selected = new Map(
			this.selectedRefundItems().map((item) => [item.orderItemId, item.quantity])
		);
		const selectedSubTotal = order.items.reduce((sum, item) => {
			const quantity = selected.get(item.id) ?? 0;
			const unitSubTotal = item.quantity > 0 ? item.subTotal / item.quantity : 0;
			return sum + unitSubTotal * quantity;
		}, 0);
		const proportionalAmount = order.totalAmount * (selectedSubTotal / order.subTotalAmount);
		const remainingAmount = Math.max(
			0,
			order.totalAmount - (order.totalRefundedAmount ?? 0)
		);
		return Math.min(remainingAmount, Math.max(0, proportionalAmount));
	});

	ngOnInit(): void {
		if (window.location.pathname.startsWith('/admin')) {
			window.history.replaceState(null, '', '/');
			this.isAdminPage.set(false);
			this.activeTab.set('pos');
		}

		window.addEventListener('popstate', () => {
			if (window.location.pathname.startsWith('/admin')) {
				window.history.replaceState(null, '', '/');
				this.isAdminPage.set(false);
				this.activeTab.set('pos');
			} else {
				this.isAdminPage.set(false);
				if (this.activeTab() === 'admin') {
					this.activeTab.set('pos');
				}
			}
		});

		window.addEventListener('click', (e) => {
			const target = e.target as HTMLElement;
			if (!target.closest('.header-menu-container')) {
				this.isActionMenuOpen.set(false);
			}
		});

		if (this.auth.isAuthenticated()) {
			this.loadAppData();
		}
	}

	protected navigateToAdmin(): void {
		window.history.pushState(null, '', '/admin');
		this.isAdminPage.set(true);
		this.activeTab.set('admin');
		this.loadPlatformDashboard();
	}

	protected navigateToPos(): void {
		window.history.pushState(null, '', '/');
		this.isAdminPage.set(false);
		this.activeTab.set('pos');
	}

	protected openManagementTab(tab: 'customers' | 'promotions' | 'shifts'): void {
		this.isAdminPage.set(false);
		this.activeTab.set(tab);
		this.isActionMenuOpen.set(false);

		if (tab === 'customers') {
			this.loadCustomers();
		} else if (tab === 'promotions') {
			this.loadPromotions();
		} else {
			this.loadCashShifts();
		}
	}

	protected toggleActionMenu(event: MouseEvent): void {
		event.stopPropagation();
		this.isActionMenuOpen.update(v => !v);
	}

	protected t(key: string): string {
		return this.i18n.translate(key);
	}

	protected toggleLang(): void {
		this.i18n.toggleLocale();
	}

	protected quickLogin(email: string): void {
		this.loginEmail.set(email);
		this.loginPassword.set('password123');
		this.handleLogin();
	}

	protected handleLogin(): void {
		this.authError.set('');
		this.api.login(this.loginEmail(), this.loginPassword()).subscribe({
			next: (res) => {
				this.auth.setSession(res);
				this.loadAppData();
			},
			error: (error) => this.authError.set(this.getErrorMessage(error, 'Login failed. Check credentials.'))
		});
	}

	protected handleRegisterStore(): void {
		this.authError.set('');
		this.api.registerStore(
			this.regStoreName(),
			this.regStoreSlug(),
			this.regOwnerEmail(),
			this.regOwnerPassword(),
			this.regOwnerName()
		).subscribe({
			next: (res) => {
				this.auth.setSession(res);
				this.isRegisterMode.set(false);
				this.loadAppData();
			},
			error: (error) => this.authError.set(this.getErrorMessage(error, 'Failed to register store.'))
		});
	}

	protected handleLogout(): void {
		this.auth.logout();
		this.cart.set([]);
	}

	protected loadAppData(): void {
		this.loadCategories();
		this.loadProducts();
		this.loadOrders();
		this.loadStoreSettings();
		this.loadCurrentCashShift();
		if (this.canManageStore()) {
			this.loadZReport();
		}
		if (this.canManageStaff()) {
			this.loadStaffUsers();
		}
	}

	protected loadCategories(): void {
		this.api.getCategories().subscribe({ next: (cats) => this.categories.set(cats) });
	}

	protected loadProducts(): void {
		this.isLoadingProducts.set(true);
		this.productsError.set('');
		this.api.getProducts(this.searchQuery(), this.selectedCategoryId()).subscribe({
			next: (prods) => { this.products.set(prods); this.isLoadingProducts.set(false); },
			error: (error) => {
				this.isLoadingProducts.set(false);
				this.productsError.set(this.getErrorMessage(error, 'Unable to load products.'));
			}
		});
	}

	protected loadOrders(): void {
		this.isLoadingOrders.set(true);
		this.ordersError.set('');
		this.api.getOrders().subscribe({
			next: (orders) => {
				this.orders.set(orders);
				this.isLoadingOrders.set(false);
			},
			error: (error) => {
				this.isLoadingOrders.set(false);
				this.ordersError.set(this.getErrorMessage(error, 'Unable to load sales orders.'));
			}
		});
	}

	protected loadZReport(): void {
		this.isLoadingZReport.set(true);
		this.zReportError.set('');
		this.api.getZReportSummary().subscribe({
			next: (z) => {
				this.zReport.set(z);
				this.isLoadingZReport.set(false);
			},
			error: (error) => {
				this.isLoadingZReport.set(false);
				this.zReportError.set(this.getErrorMessage(error, 'Unable to load the Z-report.'));
			}
		});
	}

	protected loadStaffUsers(): void {
		this.api.getStaffUsers().subscribe({ next: (users) => this.staffUsers.set(users) });
	}

	protected loadAuditLogs(): void {
		this.api.getAuditLogs().subscribe({ next: (logs) => this.auditLogs.set(logs) });
	}

	protected loadStoreSettings(): void {
		this.api.getStoreSettings().subscribe({
			next: (res) => {
				this.storeSettings.set(res);
				this.settingsQrCodeUrl.set(res.qrCodeUrl);
				this.settingsVatRate.set(res.vatRate);
				this.settingsServiceChargeRate.set(res.serviceChargeRate);
				this.settingsReceiptHeader.set(res.receiptHeaderNote);
				this.settingsReceiptFooter.set(res.receiptFooterNote);
				this.settingsBusinessTimeZoneId.set(res.businessTimeZoneId);
			}
		});
	}

	protected openStoreSettings(): void {
		this.loadStoreSettings();
		this.isStoreSettingsModalOpen.set(true);
	}

	protected saveStoreSettings(): void {
		this.api.updateStoreSettings({
			qrCodeUrl: this.settingsQrCodeUrl(),
			vatRate: Number(this.settingsVatRate()),
			serviceChargeRate: Number(this.settingsServiceChargeRate()),
			receiptHeaderNote: this.settingsReceiptHeader(),
			receiptFooterNote: this.settingsReceiptFooter(),
			businessTimeZoneId: this.settingsBusinessTimeZoneId().trim()
		}).subscribe({
			next: (res) => {
				this.storeSettings.set(res);
				this.isStoreSettingsModalOpen.set(false);
			}
		});
	}

	protected onQrCodeFileSelected(event: Event): void {
		const input = event.target as HTMLInputElement;
		if (input.files && input.files[0]) {
			const file = input.files[0];
			const reader = new FileReader();
			reader.onload = (e) => {
				const img = new Image();
				img.onload = () => {
					const canvas = document.createElement('canvas');
					const ctx = canvas.getContext('2d');
					const maxDim = 500;
					let width = img.width;
					let height = img.height;

					if (width > height) {
						if (width > maxDim) {
							height = Math.round((height * maxDim) / width);
							width = maxDim;
						}
					} else {
						if (height > maxDim) {
							width = Math.round((width * maxDim) / height);
							height = maxDim;
						}
					}

					canvas.width = width;
					canvas.height = height;
					ctx?.drawImage(img, 0, 0, width, height);
					const compressedBase64 = canvas.toDataURL('image/jpeg', 0.85);
					this.settingsQrCodeUrl.set(compressedBase64);
				};
				img.src = e.target?.result as string;
			};
			reader.readAsDataURL(file);
		}
	}

	protected selectActiveStaffUser(staff: StaffUser): void {
		this.activeStaffUser.set(staff);
		this.isSwitchStaffModalOpen.set(false);
	}

	protected loadCurrentCashShift(): void {
		this.isLoadingCashShift.set(true);
		this.shiftError.set('');
		this.api.getCurrentCashShift().subscribe({
			next: (shift) => {
				this.currentCashShift.set(shift?.status === 'Open' ? shift : null);
				this.isLoadingCashShift.set(false);
			},
			error: (error) => {
				this.isLoadingCashShift.set(false);
				this.shiftError.set(this.getErrorMessage(error, 'Unable to check the current cash shift.'));
			}
		});
	}

	protected loadCashShifts(): void {
		this.isLoadingCashShift.set(true);
		this.shiftError.set('');
		this.api.getCashShifts().subscribe({
			next: (shifts) => {
				this.cashShifts.set(shifts);
				this.currentCashShift.set(shifts.find((shift) => shift.status === 'Open') ?? null);
				this.isLoadingCashShift.set(false);
			},
			error: (error) => {
				this.isLoadingCashShift.set(false);
				this.shiftError.set(this.getErrorMessage(error, 'Unable to load cash shift history.'));
			}
		});
	}

	protected openShiftModal(mode: 'Open' | 'Close'): void {
		this.shiftError.set('');
		this.shiftSuccess.set('');
		this.shiftModalMode.set(mode);
		this.shiftIdempotencyKey.set(
			this.createIdempotencyKey(
				mode === 'Open' ? 'shift-open' : `shift-close-${this.currentCashShift()?.id ?? 0}`
			)
		);
		if (mode === 'Open') {
			this.openingCash.set(0);
			this.shiftOpeningNote.set('');
		} else {
			this.countedCash.set(this.currentCashShift()?.expectedCash ?? 0);
			this.shiftNotes.set('');
		}
		this.isShiftModalOpen.set(true);
	}

	protected saveCashShift(): void {
		if (this.isSavingShift()) return;
		this.shiftError.set('');
		this.shiftSuccess.set('');
		this.isSavingShift.set(true);

		if (this.shiftModalMode() === 'Open') {
			this.api.openCashShift({
				openingCash: Math.max(0, Number(this.openingCash())),
				openingNote: this.shiftOpeningNote().trim() || undefined,
				idempotencyKey: this.shiftIdempotencyKey()
			}).subscribe({
				next: (shift) => this.finishShiftSave(shift, 'Cash shift opened.'),
				error: (error) => this.failShiftSave(error)
			});
			return;
		}

		const shift = this.currentCashShift();
		if (!shift) {
			this.isSavingShift.set(false);
			this.shiftError.set('There is no open cash shift to close.');
			return;
		}

		this.api.closeCashShift(shift.id, {
			closingCash: Math.max(0, Number(this.countedCash())),
			closingNote: this.shiftNotes().trim() || undefined,
			idempotencyKey: this.shiftIdempotencyKey()
		}).subscribe({
			next: (closedShift) => this.finishShiftSave(closedShift, 'Cash shift closed and reconciled.'),
			error: (error) => this.failShiftSave(error)
		});
	}

	protected loadCustomers(): void {
		this.customerError.set('');
		this.api.getCustomers().subscribe({
			next: (customers) => this.customers.set(customers),
			error: (error) => {
				this.customerError.set(this.getErrorMessage(error, 'Unable to load members.'));
			}
		});
	}

	protected searchCustomers(): void {
		const phone = this.customerSearchPhone().trim();
		if (phone.length < 3) {
			this.customerSearchResults.set([]);
			this.customerError.set('Enter at least 3 digits of the member phone number.');
			return;
		}

		this.isSearchingCustomers.set(true);
		this.customerError.set('');
		this.api.searchCustomer(phone).subscribe({
			next: (customer) => {
				this.customerSearchResults.set([customer]);
				this.isSearchingCustomers.set(false);
			},
			error: (error) => {
				this.isSearchingCustomers.set(false);
				this.customerSearchResults.set([]);
				const response = error as { status?: number };
				this.customerError.set(
					response.status === 404
						? 'No member was found for this phone number.'
						: this.getErrorMessage(error, 'Member search failed.')
				);
			}
		});
	}

	protected selectCustomer(customer: Customer): void {
		this.selectedCustomer.set(customer);
		this.redeemPoints.set(0);
		this.customerError.set('');
		this.refreshCheckoutQuote();
	}

	protected clearSelectedCustomer(): void {
		this.selectedCustomer.set(null);
		this.redeemPoints.set(0);
		this.refreshCheckoutQuote();
	}

	protected openCreateCustomer(): void {
		this.customerError.set('');
		this.newCustomerName.set('');
		this.newCustomerPhone.set(this.customerSearchPhone().trim());
		this.newCustomerEmail.set('');
		this.isCustomerModalOpen.set(true);
	}

	protected saveCustomer(): void {
		if (this.isSavingCustomer()) return;
		if (!this.newCustomerName().trim() || this.newCustomerPhone().trim().length < 8) {
			this.customerError.set('Enter the member name and a valid phone number.');
			return;
		}

		this.isSavingCustomer.set(true);
		this.customerError.set('');
		this.api.createCustomer({
			phone: this.newCustomerPhone().trim(),
			name: this.newCustomerName().trim(),
			email: this.newCustomerEmail().trim() || null
		}).subscribe({
			next: (customer) => {
				this.isSavingCustomer.set(false);
				this.isCustomerModalOpen.set(false);
				this.selectedCustomer.set(customer);
				this.customerSearchPhone.set(customer.phone);
				this.customerSearchResults.set([customer]);
				this.loadCustomers();
				this.refreshCheckoutQuote();
			},
			error: (error) => {
				this.isSavingCustomer.set(false);
				this.customerError.set(this.getErrorMessage(error, 'Unable to create the member.'));
			}
		});
	}

	protected loadPromotions(): void {
		this.isLoadingPromotions.set(true);
		this.promotionError.set('');
		this.api.getPromotions().subscribe({
			next: (promotions) => {
				this.promotions.set(promotions);
				this.isLoadingPromotions.set(false);
			},
			error: (error) => {
				this.isLoadingPromotions.set(false);
				this.promotionError.set(this.getErrorMessage(error, 'Unable to load promotions.'));
			}
		});
	}

	protected openAddPromotion(): void {
		this.editingPromotionId.set(0);
		this.promoCode.set('');
		this.promoName.set('');
		this.promoDiscountType.set('Percentage');
		this.promoDiscountValue.set(10);
		this.promoMinimumSpend.set(0);
		this.promoMaximumDiscount.set(null);
		this.promoStartsAt.set('');
		this.promoEndsAt.set('');
		this.promoUsageLimit.set(null);
		this.promoIsActive.set(true);
		this.promotionError.set('');
		this.isPromotionModalOpen.set(true);
	}

	protected editPromotion(promotion: Promotion): void {
		this.editingPromotionId.set(promotion.id);
		this.promoCode.set(promotion.code);
		this.promoName.set(promotion.name);
		this.promoDiscountType.set(promotion.discountType);
		this.promoDiscountValue.set(promotion.value);
		this.promoMinimumSpend.set(promotion.minimumOrderAmount);
		this.promoMaximumDiscount.set(promotion.maximumDiscountAmount);
		this.promoStartsAt.set(this.toDateTimeLocal(promotion.validFrom));
		this.promoEndsAt.set(this.toDateTimeLocal(promotion.validUntil));
		this.promoUsageLimit.set(promotion.usageLimit ?? null);
		this.promoIsActive.set(promotion.isActive);
		this.promotionError.set('');
		this.isPromotionModalOpen.set(true);
	}

	protected closePromotionModal(): void {
		if (this.isSavingPromotion()) return;
		this.promotionError.set('');
		this.isPromotionModalOpen.set(false);
	}

	protected savePromotion(): void {
		if (this.isSavingPromotion()) return;
		if (!this.promoCode().trim() || !this.promoName().trim()) {
			this.promotionError.set('Promotion code and campaign name are required.');
			return;
		}
		if (
			this.promoDiscountValue() <= 0 ||
			(this.promoDiscountType() === 'Percentage' && this.promoDiscountValue() > 100)
		) {
			this.promotionError.set('Enter a valid discount value.');
			return;
		}

		this.isSavingPromotion.set(true);
		this.promotionError.set('');
		const request = {
			code: this.promoCode().trim().toUpperCase(),
			name: this.promoName().trim(),
			discountType: this.promoDiscountType(),
			value: Number(this.promoDiscountValue()),
			minimumOrderAmount: Math.max(0, Number(this.promoMinimumSpend())),
			maximumDiscountAmount: this.promoMaximumDiscount(),
			validFrom: this.toUtcIso(this.promoStartsAt()),
			validUntil: this.toUtcIso(this.promoEndsAt()),
			usageLimit: this.promoUsageLimit(),
			isActive: this.promoIsActive()
		};
		const request$ = this.editingPromotionId() > 0
			? this.api.updatePromotion(this.editingPromotionId(), request)
			: this.api.createPromotion(request);

		request$.subscribe({
			next: () => {
				this.isSavingPromotion.set(false);
				this.isPromotionModalOpen.set(false);
				this.loadPromotions();
			},
			error: (error) => {
				this.isSavingPromotion.set(false);
				this.promotionError.set(this.getErrorMessage(error, 'Unable to save the promotion.'));
			}
		});
	}

	protected deletePromotion(promotion: Promotion): void {
		if (!window.confirm(`Deactivate coupon ${promotion.code}?`)) return;
		this.promotionError.set('');
		this.api.deletePromotion(promotion.id).subscribe({
			next: () => this.loadPromotions(),
			error: (error) => {
				this.promotionError.set(this.getErrorMessage(error, 'Unable to deactivate the promotion.'));
			}
		});
	}

	protected applyCoupon(): void {
		if (!this.couponCode().trim()) {
			this.quoteError.set('Enter a coupon code.');
			return;
		}
		this.refreshCheckoutQuote(this.couponCode().trim().toUpperCase());
	}

	protected clearCoupon(): void {
		this.couponCode.set('');
		this.appliedCouponCode.set(null);
		this.refreshCheckoutQuote(null);
	}

	protected refreshCheckoutQuote(couponOverride?: string | null): void {
		if (this.cart().length === 0) {
			this.checkoutQuote.set(null);
			this.appliedCouponCode.set(null);
			this.quoteError.set('');
			return;
		}

		const requestedCoupon = couponOverride === undefined
			? this.appliedCouponCode()
			: couponOverride;
		this.isLoadingCheckoutQuote.set(true);
		this.quoteError.set('');
		this.api.getOrderQuote({
			items: this.cart().map((item) => ({
				productId: item.product.id,
				quantity: item.quantity
			})),
			discountAmount: Math.max(0, Number(this.discountAmount())),
			customerPhone: this.selectedCustomer()?.phone ?? null,
			couponCode: requestedCoupon,
			loyaltyPointsToRedeem: Math.max(0, Number(this.redeemPoints()))
		}).subscribe({
			next: (quote) => {
				this.checkoutQuote.set(quote);
				this.appliedCouponCode.set(quote.couponCode);
				this.couponCode.set(quote.couponCode ?? '');
				this.redeemPoints.set(quote.loyaltyPointsRedeemed);
				if (this.isCheckoutModalOpen()) {
					this.paidAmount.set(quote.totalAmount);
				}
				this.isLoadingCheckoutQuote.set(false);
			},
			error: (error) => {
				this.checkoutQuote.set(null);
				if (couponOverride !== undefined) {
					this.appliedCouponCode.set(null);
				}
				this.isLoadingCheckoutQuote.set(false);
				this.quoteError.set(this.getErrorMessage(error, 'Unable to verify the checkout total.'));
			}
		});
	}

	protected openOrderAction(order: Order, action: OrderAction): void {
		if (!this.canManageOrderActions()) return;
		if (action === 'Void' && order.status !== 'Completed') return;
		if (action === 'Refund' && !this.canRefundOrder(order)) return;
		this.selectedOrder.set(order);
		this.orderAction.set(action);
		this.orderActionReason.set('');
		this.orderActionError.set('');
		this.refundQuantities.set({});
		this.orderActionIdempotencyKey.set(
			this.createIdempotencyKey(`${action.toLowerCase()}-${order.id}`)
		);
		this.isOrderActionModalOpen.set(true);
	}

	protected canRefundOrder(order: Order): boolean {
		return (
			this.canManageOrderActions() &&
			(order.status === 'Completed' || order.status === 'PartiallyRefunded') &&
			order.items.some((item) => this.getRefundableQuantity(item) > 0)
		);
	}

	protected getRefundableQuantity(item: OrderItem): number {
		const available = Number.isFinite(item.refundableQuantity)
			? item.refundableQuantity
			: item.quantity - (item.refundedQuantity ?? 0);
		return Math.max(0, Math.min(item.quantity, Math.floor(available)));
	}

	protected setRefundQuantity(item: OrderItem, value: number): void {
		const quantity = this.clampRefundQuantity(value, this.getRefundableQuantity(item));
		this.refundQuantities.update((quantities) => ({ ...quantities, [item.id]: quantity }));
		this.orderActionError.set('');
	}

	protected adjustRefundQuantity(item: OrderItem, delta: number): void {
		this.setRefundQuantity(item, (this.refundQuantities()[item.id] ?? 0) + delta);
	}

	protected selectAllRefundItems(): void {
		const order = this.selectedOrder();
		if (!order) return;
		this.refundQuantities.set(
			Object.fromEntries(
				order.items.map((item) => [item.id, this.getRefundableQuantity(item)])
			)
		);
		this.orderActionError.set('');
	}

	protected clearRefundItems(): void {
		this.refundQuantities.set({});
	}

	protected closeOrderActionModal(): void {
		if (this.isProcessingOrderAction()) return;
		this.isOrderActionModalOpen.set(false);
		this.orderActionError.set('');
		this.refundQuantities.set({});
	}

	protected submitOrderAction(): void {
		if (this.isProcessingOrderAction()) return;
		const order = this.selectedOrder();
		if (!order || !this.canManageOrderActions()) {
			this.orderActionError.set('Only a manager or owner can update completed orders.');
			return;
		}
		if (this.orderActionReason().trim().length < 3) {
			this.orderActionError.set('Enter a clear reason of at least 3 characters.');
			return;
		}
		if (this.orderAction() === 'Refund' && this.selectedRefundItems().length === 0) {
			this.orderActionError.set(this.t('orders.selectAtLeastOne'));
			return;
		}

		this.isProcessingOrderAction.set(true);
		this.orderActionError.set('');
		const baseRequest = {
			reason: this.orderActionReason().trim(),
			idempotencyKey: this.orderActionIdempotencyKey()
		};
		const request$ = this.orderAction() === 'Void'
			? this.api.voidOrder(order.id, baseRequest)
			: this.api.refundOrder(order.id, {
				...baseRequest,
				items: this.selectedRefundItems()
			});

		request$.subscribe({
			next: (reversal) => {
				this.isProcessingOrderAction.set(false);
				this.isOrderActionModalOpen.set(false);
				this.orderActionIdempotencyKey.set('');
				this.refundQuantities.set({});
				this.orderActionSuccess.set(
					this.orderAction() === 'Void'
						? 'Order voided and stock restored.'
						: `${this.t('orders.partialRefundSuccess')} THB ${reversal.amount.toFixed(2)}`
				);
				this.loadOrders();
				this.loadProducts();
				this.loadCurrentCashShift();
				this.loadCustomers();
				if (this.canManageStore()) this.loadZReport();
			},
			error: (error) => {
				this.isProcessingOrderAction.set(false);
				this.orderActionError.set(this.getErrorMessage(error, 'Unable to update the order.'));
			}
		});
	}

	private clampRefundQuantity(value: number, maximum: number): number {
		const numericValue = Number(value);
		if (!Number.isFinite(numericValue)) return 0;
		return Math.max(0, Math.min(maximum, Math.floor(numericValue)));
	}

	protected loadPlatformDashboard(): void {
		this.api.getPlatformDashboard().subscribe({ next: (data) => this.platformDashboard.set(data) });
	}

	protected addToCart(product: Product): void {
		if (product.stockQuantity <= 0) return;
		this.cart.update((items) => {
			const existingIndex = items.findIndex((i) => i.product.id === product.id);
			if (existingIndex > -1) {
				const currentQty = items[existingIndex].quantity;
				if (currentQty >= product.stockQuantity) return items;
				return items.map((item, idx) =>
					idx === existingIndex
						? { ...item, quantity: currentQty + 1, subTotal: (currentQty + 1) * product.price }
						: item
				);
			}
			return [...items, { product, quantity: 1, subTotal: product.price }];
		});
		this.invalidateCheckoutQuote();
	}

	protected updateCartQty(productId: number, delta: number): void {
		this.cart.update((items) =>
			items.map((item) => {
				if (item.product.id === productId) {
					const newQty = item.quantity + delta;
					if (newQty <= 0) return null;
					if (newQty > item.product.stockQuantity) return item;
					return { ...item, quantity: newQty, subTotal: newQty * item.product.price };
				}
				return item;
			}).filter(Boolean) as CartItem[]
		);
		this.invalidateCheckoutQuote();
	}

	protected updateManualDiscount(value: number): void {
		this.discountAmount.set(Math.max(0, Number(value) || 0));
		this.invalidateCheckoutQuote();
	}

	protected selectPaymentMethod(method: PaymentMethod): void {
		this.selectedPaymentMethod.set(method);
		this.paidAmount.set(this.cartFinalTotal());
		this.checkoutError.set('');
	}

	protected openCheckout(): void {
		if (this.cart().length === 0) return;
		this.checkoutError.set('');
		this.checkoutIdempotencyKey.set(this.createIdempotencyKey('checkout'));
		this.paidAmount.set(this.cartFinalTotal());
		this.isCheckoutModalOpen.set(true);
		this.refreshCheckoutQuote();
	}

	protected processPayment(): void {
		if (this.isProcessingPayment()) return;
		if (this.selectedPaymentMethod() === 'Cash' && !this.hasOpenCashShift()) {
			this.checkoutError.set('Open a cash shift before accepting a cash payment.');
			return;
		}
		if (this.isLoadingCheckoutQuote() || !this.checkoutQuote()) {
			this.checkoutError.set('Wait for the verified server total before completing payment.');
			this.refreshCheckoutQuote();
			return;
		}
		if (this.paidAmount() < this.cartFinalTotal()) {
			this.checkoutError.set('Amount received is lower than the current total.');
			return;
		}

		this.checkoutError.set('');
		this.isProcessingPayment.set(true);
		this.api.checkout({
			items: this.cart().map((item) => ({ productId: item.product.id, quantity: item.quantity })),
			discountAmount: this.discountAmount(),
			paidAmount: this.paidAmount(),
			paymentMethod: this.selectedPaymentMethod(),
			idempotencyKey: this.checkoutIdempotencyKey(),
			customerPhone: this.selectedCustomer()?.phone ?? null,
			couponCode: this.appliedCouponCode(),
			loyaltyPointsToRedeem: Math.max(0, Number(this.redeemPoints()))
		}).subscribe({
			next: (order) => {
				this.completedOrder.set(order);
				this.cart.set([]);
				this.discountAmount.set(0);
				this.selectedCustomer.set(null);
				this.couponCode.set('');
				this.appliedCouponCode.set(null);
				this.redeemPoints.set(0);
				this.checkoutQuote.set(null);
				this.checkoutIdempotencyKey.set('');
				this.paidAmount.set(0);
				this.isCheckoutModalOpen.set(false);
				this.isSlipModalOpen.set(true);
				this.isProcessingPayment.set(false);
				this.loadAppData();
			},
			error: (err) => {
				this.isProcessingPayment.set(false);
				this.checkoutError.set(
					this.getErrorMessage(err, 'Payment could not be completed. Review the order and try again.')
				);
				this.checkoutQuote.set(null);
				this.refreshCheckoutQuote();
			}
		});
	}

	protected openAddProduct(): void {
		this.editingProductId.set(0);
		this.prodBarcode.set(`885${Math.floor(1000 + Math.random() * 9000)}`);
		this.prodName.set('');
		this.prodPrice.set(50);
		this.prodCost.set(20);
		this.prodStock.set(50);
		this.prodImageUrl.set('');
		this.isProductModalOpen.set(true);
	}

	protected editProduct(prod: Product): void {
		this.editingProductId.set(prod.id);
		this.prodCategoryId.set(prod.categoryId);
		this.prodBarcode.set(prod.barcode);
		this.prodName.set(prod.name);
		this.prodPrice.set(prod.price);
		this.prodCost.set(prod.cost);
		this.prodStock.set(prod.stockQuantity);
		this.prodImageUrl.set(prod.imageUrl || '');
		this.isProductModalOpen.set(true);
	}

	protected onFileSelected(event: Event): void {
		const input = event.target as HTMLInputElement;
		if (input.files && input.files[0]) {
			const file = input.files[0];
			const reader = new FileReader();
			reader.onload = (e) => {
				const img = new Image();
				img.onload = () => {
					const canvas = document.createElement('canvas');
					const MAX_SIZE = 600;
					let width = img.width;
					let height = img.height;

					if (width > height) {
						if (width > MAX_SIZE) {
							height *= MAX_SIZE / width;
							width = MAX_SIZE;
						}
					} else {
						if (height > MAX_SIZE) {
							width *= MAX_SIZE / height;
							height = MAX_SIZE;
						}
					}

					canvas.width = width;
					canvas.height = height;
					const ctx = canvas.getContext('2d');
					if (ctx) {
						ctx.fillStyle = '#FFFFFF';
						ctx.fillRect(0, 0, width, height);
						ctx.drawImage(img, 0, 0, width, height);
					}

					const resizedBase64 = canvas.toDataURL('image/jpeg', 0.82);
					this.prodImageUrl.set(resizedBase64);
				};
				img.src = e.target?.result as string;
			};
			reader.readAsDataURL(file);
		}
	}

	protected saveProduct(): void {
		this.api.saveProduct({
			id: this.editingProductId(),
			categoryId: this.prodCategoryId(),
			barcode: this.prodBarcode(),
			name: this.prodName(),
			price: this.prodPrice(),
			cost: this.prodCost(),
			stockQuantity: this.prodStock(),
			minimumStock: 5,
			unit: 'pcs',
			imageUrl: this.prodImageUrl()
		}).subscribe({
			next: () => {
				this.isProductModalOpen.set(false);
				this.loadProducts();
			}
		});
	}

	protected confirmDeleteProduct(productId: number): void {
		this.deletingProductId.set(productId);
		this.isDeleteConfirmOpen.set(true);
	}

	protected executeDeleteProduct(): void {
		this.api.deleteProduct(this.deletingProductId()).subscribe({
			next: () => {
				this.isDeleteConfirmOpen.set(false);
				this.loadProducts();
			}
		});
	}

	protected openAddStaff(): void {
		this.editingStaffId.set(0);
		this.staffEmail.set('');
		this.staffPassword.set('password123');
		this.staffFullName.set('');
		this.staffEmployeeCode.set(`${String(this.staffUsers().length + 1).padStart(3, '0')}`);
		this.staffPositionTitle.set('');
		this.staffRole.set('Cashier');
		this.staffCanCheckout.set(true);
		this.staffCanProducts.set(false);
		this.staffCanReports.set(false);
		this.staffCanUsers.set(false);
		this.isStaffModalOpen.set(true);
	}

	protected editStaff(staff: StaffUser): void {
		this.editingStaffId.set(staff.id);
		this.staffEmail.set(staff.email);
		this.staffFullName.set(staff.fullName);
		this.staffEmployeeCode.set(staff.employeeCode);
		this.staffPositionTitle.set(staff.positionTitle);
		this.staffRole.set(staff.role);
		this.staffCanCheckout.set(staff.canProcessCheckout);
		this.staffCanProducts.set(staff.canManageProducts);
		this.staffCanReports.set(staff.canViewReports);
		this.staffCanUsers.set(staff.canManageUsers);
		this.isStaffModalOpen.set(true);
	}

	protected saveStaff(): void {
		if (this.editingStaffId() > 0) {
			this.api.updateStaff(this.editingStaffId(), {
				fullName: this.staffFullName(),
				employeeCode: this.staffEmployeeCode(),
				positionTitle: this.staffPositionTitle(),
				role: this.staffRole(),
				canProcessCheckout: this.staffCanCheckout(),
				canManageProducts: this.staffCanProducts(),
				canViewReports: this.staffCanReports(),
				canManageUsers: this.staffCanUsers()
			}).subscribe({
				next: () => { this.isStaffModalOpen.set(false); this.loadStaffUsers(); }
			});
		} else {
			this.api.createStaff({
				email: this.staffEmail(),
				password: this.staffPassword(),
				fullName: this.staffFullName(),
				employeeCode: this.staffEmployeeCode(),
				positionTitle: this.staffPositionTitle(),
				role: this.staffRole(),
				canProcessCheckout: this.staffCanCheckout(),
				canManageProducts: this.staffCanProducts(),
				canViewReports: this.staffCanReports(),
				canManageUsers: this.staffCanUsers()
			}).subscribe({
				next: () => { this.isStaffModalOpen.set(false); this.loadStaffUsers(); }
			});
		}
	}

	protected deleteStaff(staffId: number): void {
		this.api.deleteStaff(staffId).subscribe({
			next: () => this.loadStaffUsers()
		});
	}

	protected invalidateCheckoutQuote(): void {
		this.checkoutQuote.set(null);
		this.quoteError.set('');
	}

	private finishShiftSave(shift: CashShift, message: string): void {
		this.isSavingShift.set(false);
		this.isShiftModalOpen.set(false);
		this.shiftIdempotencyKey.set('');
		this.shiftSuccess.set(message);
		this.currentCashShift.set(shift.status === 'Open' ? shift : null);
		this.loadCashShifts();
	}

	private failShiftSave(error: unknown): void {
		this.isSavingShift.set(false);
		this.shiftError.set(this.getErrorMessage(error, 'Unable to save the cash shift.'));
	}

	private createIdempotencyKey(prefix: string): string {
		const suffix = globalThis.crypto?.randomUUID?.() ??
			`${Date.now()}-${Math.random().toString(36).slice(2)}`;
		return `${prefix}-${suffix}`;
	}

	private toDateTimeLocal(value: string | null | undefined): string {
		if (!value) return '';
		const date = new Date(value);
		if (Number.isNaN(date.getTime())) return '';
		const offset = date.getTimezoneOffset() * 60_000;
		return new Date(date.getTime() - offset).toISOString().slice(0, 16);
	}

	private toUtcIso(value: string | null | undefined): string | null {
		if (!value) return null;
		const date = new Date(value);
		return Number.isNaN(date.getTime()) ? null : date.toISOString();
	}

	private getErrorMessage(error: unknown, fallback: string): string {
		const response = error as { error?: { detail?: string; message?: string; title?: string } };
		return response.error?.detail || response.error?.message || response.error?.title || fallback;
	}

	protected exportCsv(): void {
		this.api.exportOrdersCsv().subscribe({
			next: (blob) => {
				const url = window.URL.createObjectURL(blob);
				const a = document.createElement('a');
				a.href = url;
				a.download = `pos-sales-${new Date().toISOString().slice(0, 10)}.csv`;
				a.click();
				window.URL.revokeObjectURL(url);
			}
		});
	}

	protected printSlip(): void {
		window.print();
	}
}
