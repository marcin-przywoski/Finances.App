window.financeCharts = {
    instances: {},

    renderLineChart: function (canvasId, labels, datasets) {
        this._destroy(canvasId);
        const ctx = document.getElementById(canvasId);
        if (!ctx) return;
        this.instances[canvasId] = new Chart(ctx, {
            type: 'line',
            data: {
                labels: labels,
                datasets: datasets.map((ds, i) => ({
                    label: ds.label,
                    data: ds.data,
                    borderColor: ds.color || this._colors(i),
                    backgroundColor: (ds.color || this._colors(i)) + '20',
                    fill: true,
                    tension: 0.3,
                    pointRadius: 3,
                    pointHoverRadius: 5
                }))
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                plugins: { legend: { position: 'top' } },
                scales: {
                    y: { beginAtZero: true, grid: { color: '#e5e7eb' } },
                    x: { grid: { display: false } }
                }
            }
        });
    },

    renderBarChart: function (canvasId, labels, datasets) {
        this._destroy(canvasId);
        const ctx = document.getElementById(canvasId);
        if (!ctx) return;
        this.instances[canvasId] = new Chart(ctx, {
            type: 'bar',
            data: {
                labels: labels,
                datasets: datasets.map((ds, i) => ({
                    label: ds.label,
                    data: ds.data,
                    backgroundColor: ds.color || this._colors(i),
                    borderRadius: 4
                }))
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                plugins: { legend: { position: 'top' } },
                scales: {
                    y: { beginAtZero: true, grid: { color: '#e5e7eb' } },
                    x: { grid: { display: false } }
                }
            }
        });
    },

    renderDoughnutChart: function (canvasId, labels, data, colors) {
        this._destroy(canvasId);
        const ctx = document.getElementById(canvasId);
        if (!ctx) return;
        this.instances[canvasId] = new Chart(ctx, {
            type: 'doughnut',
            data: {
                labels: labels,
                datasets: [{
                    data: data,
                    backgroundColor: colors || labels.map((_, i) => this._colors(i)),
                    borderWidth: 2,
                    borderColor: '#fff'
                }]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                plugins: {
                    legend: { position: 'right' }
                }
            }
        });
    },

    _destroy: function (canvasId) {
        if (this.instances[canvasId]) {
            this.instances[canvasId].destroy();
            delete this.instances[canvasId];
        }
    },

    _colors: function (i) {
        const palette = ['#0d9488', '#3b82f6', '#f59e0b', '#ef4444', '#8b5cf6', '#ec4899', '#10b981', '#6366f1'];
        return palette[i % palette.length];
    }
};
