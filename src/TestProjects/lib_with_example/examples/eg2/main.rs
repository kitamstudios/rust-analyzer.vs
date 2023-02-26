use std;
use lib_with_example::add;

fn main() {
    println!("directory example: 2 + 3 = {}.", add(2, 3));

    println!("A B = {}", std::env::var_os("A B").unwrap_or(std::ffi::OsString::from("NOT FOUND!")).to_str().unwrap());
    println!("XX = {}", std::env::var("XX").unwrap_or("NOT FOUND!".to_string()));
    println!("A = {}", std::env::var("A").unwrap_or("NOT FOUND!".to_string()));

    let args: Vec<String> = std::env::args().collect();
    println!("command line: {:?}", args);

    let mut user_input = String::new();
    std::io::stdin().read_line(&mut user_input);
}
