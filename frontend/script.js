const searchInput = document.getElementById("searchInput");
const bookRows = document.querySelectorAll("#bookTable tr");
const noResults = document.getElementById("noResults");

searchInput.addEventListener("input", function () {
    const searchText = searchInput.value.trim().toLowerCase();
    let matchingBooks = 0;

    bookRows.forEach(function (row) {
        const title = row.cells[1].textContent.toLowerCase();
        const author = row.cells[2].textContent.toLowerCase();

        const matches =
            title.includes(searchText) || author.includes(searchText);

        row.hidden = !matches;

        if (matches) {
            matchingBooks++;
        }
    });

    noResults.hidden = matchingBooks > 0;
});