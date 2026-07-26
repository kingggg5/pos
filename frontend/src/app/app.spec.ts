import { TestBed } from '@angular/core/testing';
import { App } from './app';
import { AuthService } from './core/services/auth.service';
import { Order } from './core/models/pos.models';

describe('App', () => {
	beforeEach(async () => {
		localStorage.clear();
		await TestBed.configureTestingModule({
			imports: [App]
		}).compileComponents();
	});

	it('creates the Smart POS shell', () => {
		const fixture = TestBed.createComponent(App);
		expect(fixture.componentInstance).toBeTruthy();
	});

	it('renders an accessible sign-in surface for signed-out users', async () => {
		const fixture = TestBed.createComponent(App);
		fixture.detectChanges();
		await fixture.whenStable();

		const compiled = fixture.nativeElement as HTMLElement;
		expect(compiled.querySelector('.logo-badge')?.textContent).toContain('SMART POS');
		expect(compiled.querySelector('input[type="email"]')).not.toBeNull();
		expect(compiled.querySelector('input[type="password"]')).not.toBeNull();
		expect(compiled.querySelector('button[type="submit"]')?.textContent).toContain(
			'Login to Store'
		);
		expect(compiled.querySelector('[role="tablist"]')).not.toBeNull();
	});

	it('uses inline SVG for the language action and contains no flag emoji', () => {
		const fixture = TestBed.createComponent(App);
		fixture.detectChanges();

		const languageButton = fixture.nativeElement.querySelector(
			'.lang-toggle-btn'
		) as HTMLButtonElement;
		expect(languageButton.querySelector('svg')).not.toBeNull();
		expect(languageButton.textContent).not.toMatch(/[\u{1F1E6}-\u{1F1FF}]/u);
	});

	it('clamps partial-refund quantities to the server-provided refundable maximum', () => {
		const fixture = TestBed.createComponent(App);
		fixture.detectChanges();
		const auth = TestBed.inject(AuthService);
		auth.setSession({
			userId: 2,
			email: 'manager@coffee.com',
			fullName: 'Store Manager',
			role: 'Manager',
			tenantId: 1,
			tenantName: 'Coffee Bar',
			tenantSlug: 'coffee-bar',
			token: 'test-token'
		});
		const item = {
			id: 71,
			productId: 7,
			productName: 'House Blend',
			barcode: '8850007',
			unitPrice: 100,
			quantity: 3,
			subTotal: 300,
			refundedQuantity: 1,
			refundableQuantity: 2
		};
		const order: Order = {
			id: 42,
			orderNo: 'ORD-0042',
			subTotalAmount: 300,
			totalAmount: 321,
			discountAmount: 0,
			paidAmount: 321,
			changeAmount: 0,
			paymentMethod: 'Cash',
			status: 'PartiallyRefunded',
			cashierName: 'Cashier',
			createdAt: '2026-07-26T07:00:00Z',
			items: [item],
			totalRefundedAmount: 107
		};
		const component = fixture.componentInstance as any;

		component.openOrderAction(order, 'Refund');
		component.setRefundQuantity(item, 99);

		expect(component.selectedRefundItems()).toEqual([
			{ orderItemId: 71, quantity: 2 }
		]);
		expect(component.selectedRefundQuantity()).toBe(2);
		expect(component.estimatedRefundAmount()).toBe(214);

		fixture.detectChanges();
		const quantityInput = fixture.nativeElement.querySelector(
			'#refund-quantity-71'
		) as HTMLInputElement;
		expect(quantityInput.max).toBe('2');
		expect(quantityInput.getAttribute('aria-describedby')).toBe('refund-help-71');
	});

	it('selects every remaining refundable item with the full-refund shortcut', () => {
		const fixture = TestBed.createComponent(App);
		const auth = TestBed.inject(AuthService);
		auth.setSession({
			userId: 1,
			email: 'owner@coffee.com',
			fullName: 'Store Owner',
			role: 'Owner',
			tenantId: 1,
			tenantName: 'Coffee Bar',
			tenantSlug: 'coffee-bar',
			token: 'test-token'
		});
		const order = {
			id: 43,
			orderNo: 'ORD-0043',
			subTotalAmount: 200,
			totalAmount: 214,
			discountAmount: 0,
			paidAmount: 214,
			changeAmount: 0,
			paymentMethod: 'PromptPay' as const,
			status: 'Completed' as const,
			cashierName: 'Cashier',
			createdAt: '2026-07-26T08:00:00Z',
			totalRefundedAmount: 0,
			items: [
				{
					id: 81,
					productId: 8,
					productName: 'Latte',
					barcode: '8850008',
					unitPrice: 100,
					quantity: 2,
					subTotal: 200,
					refundedQuantity: 0,
					refundableQuantity: 2
				}
			]
		};
		const component = fixture.componentInstance as any;

		component.openOrderAction(order, 'Refund');
		component.selectAllRefundItems();

		expect(component.selectedRefundItems()).toEqual([
			{ orderItemId: 81, quantity: 2 }
		]);
	});
});
