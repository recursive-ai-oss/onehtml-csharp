namespace RecursiveAI.OneHtml.Accessibility.Tests;

/// <summary>Pages written for the tests: realistic markup with the structures a tree and a reading view have to handle.</summary>
internal static class RealisticPages
{
    public static readonly (string Name, string Html)[] All =
    [
        ("school", School),
        ("forms", Forms),
        ("dashboard", Dashboard),
        ("tables", Tables),
        ("lists", Lists),
        ("landmarks", Landmarks),
        ("dialog-lang", DialogLang),
    ];

    public const string School = """
        <!doctype html>
        <html lang="en"><head><meta charset="utf-8"><title>Maple Grove High School</title>
        <style>
        .skip{position:absolute;left:-999px} nav a{color:#036} .badge::before{content:"New: "} .hide{display:none}
        li.step::before{content:counter(s) ". ";counter-increment:s} ol.steps{counter-reset:s}
        </style></head>
        <body>
        <a class="skip" href="#main">Skip to content</a>
        <header><h1>Maple Grove High School</h1>
        <nav aria-label="Primary"><ul><li><a href="/about">About</a></li><li><a href="/calendar" class="badge">Calendar</a></li><li><a href="/contact">Contact</a></li></ul></nav></header>
        <main id="main">
          <h2 id="news">News</h2>
          <article><h3>Spring concert</h3><p>The concert is on <time datetime="2026-05-02">May 2</time>. <a href="/concert">Details<span class="hide"> hidden</span></a></p></article>
          <article><h3>Enrollment</h3><ol class="steps"><li class="step">Fill the form</li><li class="step">Bring documents</li></ol></article>
          <section aria-labelledby="news"><p>Latest news</p></section>
          <table><caption>Bus schedule</caption><thead><tr><th scope="col">Route</th><th scope="col">Time</th></tr></thead>
          <tbody><tr><th scope="row">12</th><td>7:40</td></tr><tr><th scope="row">14</th><td>7:55</td></tr></tbody></table>
        </main>
        <aside aria-label="Links"><h2>Links</h2><ul><li><a href="https://example.org">District</a></li></ul></aside>
        <footer><p>&copy; Maple Grove</p></footer>
        </body></html>
        """;

    public const string Forms = """
        <!doctype html><html lang="en"><head><title>Contact</title></head><body>
        <form id="f" aria-label="Contact form">
          <fieldset><legend>Your details</legend>
          <label for="name">Name</label><input id="name" required aria-describedby="nh">
          <span id="nh">Full name</span>
          <label>Email <input type="email" id="email" aria-invalid="true" aria-errormessage="eerr"></label>
          <span id="eerr" role="alert">Enter a valid address</span>
          <label for="topic">Topic</label>
          <select id="topic"><option>General<option selected>Billing</select>
          <input type="radio" name="r" id="r1" checked><label for="r1">Yes</label>
          <input type="radio" name="r" id="r2"><label for="r2">No</label>
          </fieldset>
          <textarea aria-label="Message" placeholder="Say hello"></textarea>
          <button type="submit">Send</button> <button type="button" disabled>Cancel</button>
        </form>
        <div role="dialog" aria-modal="true" aria-labelledby="dt"><h2 id="dt">Confirm</h2><p>Sure?</p><button>OK</button></div>
        <p lang="fr">Bonjour <span lang="de">Welt</span></p>
        </body></html>
        """;

    public const string Dashboard = """
        <!doctype html><html><head><title>Dash</title><style>.tab::after{content:" (tab)"} [aria-hidden=true]{display:none}</style></head><body>
        <div role="tablist" aria-label="Views"><button role="tab" aria-selected="true" id="t1" aria-controls="p1" class="tab">One</button><button role="tab" aria-selected="false" id="t2" aria-controls="p2" class="tab">Two</button></div>
        <div role="tabpanel" id="p1" aria-labelledby="t1">Panel one <a href="#">go</a></div>
        <div role="tabpanel" id="p2" aria-labelledby="t2" hidden>Panel two</div>
        <ul role="menu" aria-label="Actions"><li role="menuitem">Open</li><li role="menuitemcheckbox" aria-checked="true">Auto</li></ul>
        <div role="grid" aria-label="Grid"><div role="row"><span role="columnheader">A</span><span role="columnheader">B</span></div><div role="row"><span role="gridcell">1</span><span role="gridcell">2</span></div></div>
        <details open><summary>More</summary><p>Hidden when closed</p></details>
        <div aria-owns="p2" id="o">Owner</div>
        <button aria-expanded="false" aria-haspopup="menu">Menu</button>
        <img src="a.png" alt="Chart"><svg role="img" aria-label="Logo"><title>x</title></svg>
        </body></html>
        """;

    public const string Tables = """
        <!doctype html><html lang="en"><head><title>Tables</title></head><body>
        <table>
          <caption>Quarterly sales</caption>
          <thead><tr><th scope="col">Region</th><th scope="col" colspan="2">Sales</th></tr>
                 <tr><td></td><th scope="col">Q1</th><th scope="col">Q2</th></tr></thead>
          <tbody>
            <tr><th scope="row">North</th><td>10</td><td>12</td></tr>
            <tr><th scope="row">South</th><td>7</td><td><a href="/south-q2">9</a></td></tr>
          </tbody>
        </table>
        <table><tr><td>Layout cell one</td><td>Layout cell two</td></tr></table>
        <table aria-label="Spans"><tr><th>A</th><th>B</th></tr><tr><td rowspan="2">x</td><td>y</td></tr><tr><td>z</td></tr></table>
        </body></html>
        """;

    public const string Lists = """
        <!doctype html><html lang="en"><head><title>Lists</title></head><body>
        <h1>Shopping</h1>
        <ul>
          <li>Fruit
            <ul><li>Apple</li><li>Pear</li></ul>
          </li>
          <li><a href="/veg">Vegetables</a></li>
          <li>Bread</li>
        </ul>
        <ol><li>First</li><li>Second</li></ol>
        <dl><dt>Term</dt><dd>Definition</dd></dl>
        <p>Plain paragraph with <b>bold</b>, <i>italic</i> and a <a href="#top">same-page link</a> inside.</p>
        <p>Second paragraph.</p>
        <h2>With <a href="/x">link</a> inside</h2>
        <h3 aria-label="Renamed">Original</h3>
        </body></html>
        """;

    public const string Landmarks = """
        <!doctype html><html lang="en"><head><title>Landmarks</title></head><body>
        <header><p>Site banner</p><nav aria-label="Main"><a href="/">Home</a> <a href="/a">A</a></nav></header>
        <main><h1>Title</h1>
          <section aria-label="Intro"><p>Intro text</p></section>
          <section><p>Unnamed section</p></section>
          <article><h2>Story</h2><p>Body</p></article>
          <aside><p>Aside text</p></aside>
          <form role="search"><label>Find <input type="search"></label><button>Go</button></form>
        </main>
        <footer><p>Footer text</p></footer>
        </body></html>
        """;

    public const string DialogLang = """
        <!doctype html><html lang="en"><head><title>Dialog</title></head><body>
        <p>English text.</p>
        <p lang="fr">Texte <em>français</em> ici.</p>
        <p>Back to <span lang="es">hola</span> English.</p>
        <div role="dialog" aria-modal="true" aria-label="Settings"><label><input type="checkbox" checked> Notifications</label>
        <button aria-pressed="true">Bold</button><button aria-expanded="false" aria-haspopup="menu">More</button></div>
        <div role="alertdialog" aria-label="Warning"><p>Careful</p></div>
        <input aria-label="Age" type="number" value="12" required aria-invalid="true">
        <select aria-label="Size"><option>S</option><option selected>M</option></select>
        <p hidden>Not shown</p><p aria-hidden="true">Also not shown</p>
        </body></html>
        """;
}
