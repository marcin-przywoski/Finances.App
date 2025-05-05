export function renderEarningsChart(canvasId, labels, workerData, salonData) {
    const ctx = document.getElementById(canvasId);
    if (!ctx) return;
    const data = {
        labels: labels,
        datasets: [
            {
                label: 'Worker €',
                data: workerData,
                backgroundColor: 'rgba(54, 162, 235, 0.6)'
            },
            {
                label: 'Salon €',
                data: salonData,
                backgroundColor: 'rgba(255, 99, 132, 0.6)'
            }
        ]
    };
    new Chart(ctx, {
        type: 'bar',
        data: data,
        options: {
            responsive: true,
            plugins: {
                legend: {
                    position: 'top'
                }
            },
            scales: {
                y: {
                    beginAtZero: true
                }
            }
        }
    });
}
