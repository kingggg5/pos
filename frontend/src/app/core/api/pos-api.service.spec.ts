import { provideHttpClient } from '@angular/common/http';
import {
	HttpTestingController,
	provideHttpClientTesting
} from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { PosApiService } from './pos-api.service';

describe('PosApiService operational contracts', () => {
	let service: PosApiService;
	let httpTesting: HttpTestingController;

	beforeEach(() => {
		TestBed.configureTestingModule({
			providers: [provideHttpClient(), provideHttpClientTesting()]
		});
		service = TestBed.inject(PosApiService);
		httpTesting = TestBed.inject(HttpTestingController);
	});

	afterEach(() => {
		httpTesting.verify();
	});

	it('opens a cash shift with the idempotency key intact', () => {
		const requestBody = {
			openingCash: 1500,
			openingNote: 'Morning drawer',
			idempotencyKey: 'shift-open-123'
		};

		service.openCashShift(requestBody).subscribe();

		const request = httpTesting.expectOne('/api/cash-shifts/open');
		expect(request.request.method).toBe('POST');
		expect(request.request.body).toEqual(requestBody);
		request.flush({});
	});

	it('requests a server-authoritative order quote', () => {
		const requestBody = {
			items: [{ productId: 7, quantity: 2 }],
			discountAmount: 0,
			customerPhone: '0812345678',
			couponCode: 'SAVE10',
			loyaltyPointsToRedeem: 25
		};

		service.getOrderQuote(requestBody).subscribe();

		const request = httpTesting.expectOne('/api/orders/quote');
		expect(request.request.method).toBe('POST');
		expect(request.request.body).toEqual(requestBody);
		request.flush({});
	});

	it('sends selected order-item quantities to the partial refund endpoint', () => {
		const requestBody = {
			items: [
				{ orderItemId: 71, quantity: 1 },
				{ orderItemId: 72, quantity: 2 }
			],
			reason: 'Duplicate checkout',
			idempotencyKey: 'refund-42-123'
		};

		service.refundOrder(42, requestBody).subscribe();

		const request = httpTesting.expectOne('/api/orders/42/refund-items');
		expect(request.request.method).toBe('POST');
		expect(request.request.body).toEqual(requestBody);
		request.flush({});
	});

	it('searches for one member by encoded phone number', () => {
		service.searchCustomer('+66 81 234 5678').subscribe();

		const request = httpTesting.expectOne(
			'/api/customers/search?phone=%2B66%2081%20234%205678'
		);
		expect(request.request.method).toBe('GET');
		request.flush({});
	});

	it('creates percentage coupon campaigns with backend field names', () => {
		const requestBody = {
			code: 'SAVE10',
			name: 'Weekend campaign',
			discountType: 'Percentage' as const,
			value: 10,
			minimumOrderAmount: 200,
			maximumDiscountAmount: 100,
			validFrom: null,
			validUntil: null,
			usageLimit: 50,
			isActive: true
		};

		service.createPromotion(requestBody).subscribe();

		const request = httpTesting.expectOne('/api/promotions');
		expect(request.request.method).toBe('POST');
		expect(request.request.body).toEqual(requestBody);
		request.flush({});
	});
});
