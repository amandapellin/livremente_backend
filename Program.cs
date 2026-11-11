using LivreMente.Api.Importing;
string[] cases = {
  "$*$-Bimodules",
  "$^3$He Universe 2020",
  "$A_{2l}^{(2)}$ at level $-l-\\frac{1}{2}$",
  "$B_c$ spectroscopy using highly improved staggered quarks",
  "$B_c \\rightarrow J/\\psi$ Form Factors for the full $q^2$ range from Lattice QCD",
  "$B_{(s)} \\to D^{(*)}_c(P,V)$ decays and effects of the next-to-leading order contributions in the perturbative QCD approach",
  "$B\\to D^\\ast\\ell\\nu$ at non-zero recoil",
  "$C^{**}$-fermi systems and detailed balance",
};
foreach (var c in cases)
  Console.WriteLine($"IN : {c}\nOUT: {TitleSanitizer.Clean(c)}\n");
